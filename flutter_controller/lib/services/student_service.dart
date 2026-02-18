import 'dart:async';
import 'dart:convert';

import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:flutter_controller/models/student_access_binding.dart';
import 'package:flutter_controller/models/student.dart';
import 'package:flutter_controller/models/student_roster_sync.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:shared_preferences/shared_preferences.dart';

class StudentService {
  static final CollectionReference<Map<String, dynamic>> _studentsCollection =
      FirebaseService.firestore.collection('students');
  static final CollectionReference<Map<String, dynamic>>
      _studentAccessBindingsCollection =
      FirebaseService.firestore.collection('student_access_bindings');

  static final StreamController<List<Student>> _studentsController =
      StreamController<List<Student>>.broadcast();
  static final StreamController<int> _pendingWritesController =
      StreamController<int>.broadcast();

  static StreamSubscription<QuerySnapshot<Map<String, dynamic>>>?
      _studentsSubscription;
  static StreamSubscription<QuerySnapshot<Map<String, dynamic>>>?
      _studentBindingsSubscription;
  static Timer? _autoReconcileTimer;

  static String? _activeTherapistId;
  static Future<void>? _bootstrapFuture;
  static List<Student> _cachedStudents = <Student>[];
  static List<StudentPendingOperation> _pendingWrites =
      <StudentPendingOperation>[];
  static int _operationCounter = 0;
  static bool _isReconciling = false;
  static bool _hasReceivedInitialRemoteSnapshot = false;
  static const Duration _autoReconcileInterval = Duration(seconds: 30);
  static const Duration _duplicateUpdateCooldown = Duration(seconds: 2);
  static final Map<String, String> _lastUpdateFingerprintByStudent =
      <String, String>{};
  static final Map<String, DateTime> _lastUpdateAtByStudent =
      <String, DateTime>{};
  static Set<String> _sharedStudentIds = <String>{};

  static String? get _currentTherapistId => FirebaseService.currentUser?.uid;

  static Stream<List<Student>> getStudents() async* {
    final therapistId = _currentTherapistId;
    if (therapistId == null) {
      yield const <Student>[];
      return;
    }

    await _ensureBootstrap(therapistId);
    yield List<Student>.unmodifiable(_cachedStudents);
    yield* _studentsController.stream;
  }

  static Stream<int> watchPendingWritesCount() async* {
    final therapistId = _currentTherapistId;
    if (therapistId == null) {
      yield 0;
      return;
    }

    await _ensureBootstrap(therapistId);
    yield _pendingWrites.length;
    yield* _pendingWritesController.stream;
  }

  static Stream<List<StudentAccessBinding>> watchCurrentUserBindings() {
    final accountId = _currentTherapistId;
    if (accountId == null) {
      return Stream.value(const <StudentAccessBinding>[]);
    }

    return _studentAccessBindingsCollection
        .where('accountId', isEqualTo: accountId)
        .snapshots()
        .map((snapshot) {
      return snapshot.docs.map(StudentAccessBinding.fromFirestore).toList();
    });
  }

  static Future<String> grantSharedStudentAccess({
    required String studentId,
    required String accountId,
    required StudentRelationRole relationRole,
    DateTime? expiresAtUtc,
    String? note,
  }) async {
    final grantedBy = _currentTherapistId;
    if (grantedBy == null) {
      throw Exception('No therapist logged in');
    }

    final docRef = _studentAccessBindingsCollection.doc();
    final binding = StudentAccessBinding(
      bindingId: docRef.id,
      studentId: studentId,
      accountId: accountId,
      relationRole: relationRole,
      status: StudentAccessBindingStatus.active,
      grantedBy: grantedBy,
      grantedAtUtc: DateTime.now().toUtc(),
      expiresAtUtc: expiresAtUtc?.toUtc(),
      note: note,
    );
    await docRef.set(binding.toFirestore());
    return docRef.id;
  }

  static Future<void> revokeSharedStudentAccess(String bindingId) async {
    await _studentAccessBindingsCollection.doc(bindingId).update({
      'status': StudentAccessBindingStatus.revoked.wireValue,
      'revokedAtUtc': DateTime.now().toUtc().toIso8601String(),
    });
  }

  static Future<StudentWriteResult> addStudent({
    required String firstName,
    required String lastName,
    String? notes,
  }) async {
    final therapistId = _currentTherapistId;
    if (therapistId == null) {
      throw Exception('No therapist logged in');
    }

    await _ensureBootstrap(therapistId);

    final studentId = _studentsCollection.doc().id;
    final nowUtc = DateTime.now().toUtc();
    final student = Student(
      id: studentId,
      firstName: firstName,
      lastName: lastName,
      notes: notes,
      createdAt: nowUtc,
      updatedAt: nowUtc,
      revision: 1,
      therapistId: therapistId,
    );

    try {
      await _studentsCollection.doc(studentId).set(student.toFirestore());
      await _commitLocalStudentUpsert(student, therapistId);
      return StudentWriteResult(
        committed: true,
        queuedForRetry: false,
        studentId: studentId,
        statusMessage: 'Student created and committed.',
        pendingWrites: _pendingWrites.length,
      );
    } catch (e) {
      final operation = StudentPendingOperation.create(
        operationId: _nextOperationId(),
        student: student,
        expectedRevision: 0,
      );
      await _queuePendingOperation(operation, therapistId);
      return StudentWriteResult(
        committed: false,
        queuedForRetry: true,
        studentId: studentId,
        statusMessage: 'Create failed remotely. Added to pending queue.',
        pendingWrites: _pendingWrites.length,
      );
    }
  }

  static Future<StudentWriteResult> updateStudent({
    required String studentId,
    required String firstName,
    required String lastName,
    String? notes,
  }) async {
    final therapistId = _currentTherapistId;
    if (therapistId == null) {
      throw Exception('No therapist logged in');
    }

    await _ensureBootstrap(therapistId);

    final existing = _findCachedStudent(studentId);
    if (existing != null && existing.therapistId != therapistId) {
      return StudentWriteResult(
        committed: false,
        queuedForRetry: false,
        studentId: studentId,
        statusMessage:
            'Shared student is read-only for this account (owner only).',
        pendingWrites: _pendingWrites.length,
      );
    }
    final updateFingerprint = _buildUpdateFingerprint(
      firstName: firstName,
      lastName: lastName,
      notes: notes,
    );
    if (_isDuplicateUpdateInCooldown(studentId, updateFingerprint)) {
      return StudentWriteResult(
        committed: true,
        queuedForRetry: false,
        studentId: studentId,
        statusMessage: 'Duplicate update skipped by cooldown.',
        pendingWrites: _pendingWrites.length,
      );
    }

    try {
      final updatedStudent = await _updateStudentRemoteWithConflictGuard(
        studentId: studentId,
        firstName: firstName,
        lastName: lastName,
        notes: notes,
        expectedRevision: existing?.revision,
      );
      await _commitLocalStudentUpsert(updatedStudent, therapistId);
      _markUpdateFingerprint(studentId, updateFingerprint);
      return StudentWriteResult(
        committed: true,
        queuedForRetry: false,
        studentId: studentId,
        statusMessage: 'Student update committed.',
        pendingWrites: _pendingWrites.length,
      );
    } on _StudentRevisionConflictException {
      await _refreshStudentsFromServerFor(therapistId);
      return StudentWriteResult(
        committed: false,
        queuedForRetry: false,
        studentId: studentId,
        statusMessage:
            'Update conflict detected. Local view refreshed from server.',
        pendingWrites: _pendingWrites.length,
      );
    } catch (e) {
      final queuedStudent = Student(
        id: studentId,
        firstName: firstName,
        lastName: lastName,
        notes: notes,
        createdAt: existing?.createdAt ?? DateTime.now().toUtc(),
        updatedAt: existing?.updatedAt ?? DateTime.now().toUtc(),
        revision: existing?.revision ?? 0,
        therapistId: existing?.therapistId ?? therapistId,
      );

      final operation = StudentPendingOperation.update(
        operationId: _nextOperationId(),
        student: queuedStudent,
        expectedRevision: existing?.revision,
      );
      await _queuePendingOperation(operation, therapistId);
      return StudentWriteResult(
        committed: false,
        queuedForRetry: true,
        studentId: studentId,
        statusMessage: 'Update failed remotely. Added to pending queue.',
        pendingWrites: _pendingWrites.length,
      );
    }
  }

  static Future<StudentWriteResult> deleteStudent(String studentId) async {
    final therapistId = _currentTherapistId;
    if (therapistId == null) {
      throw Exception('No therapist logged in');
    }

    await _ensureBootstrap(therapistId);
    final existing = _findCachedStudent(studentId);
    if (existing != null && existing.therapistId != therapistId) {
      return StudentWriteResult(
        committed: false,
        queuedForRetry: false,
        studentId: studentId,
        statusMessage:
            'Shared student is read-only for this account (owner only).',
        pendingWrites: _pendingWrites.length,
      );
    }

    try {
      await _deleteStudentRemoteWithConflictGuard(
        studentId: studentId,
        expectedRevision: existing?.revision,
      );
      await _commitLocalStudentDelete(studentId, therapistId);
      return StudentWriteResult(
        committed: true,
        queuedForRetry: false,
        studentId: studentId,
        statusMessage: 'Student delete committed.',
        pendingWrites: _pendingWrites.length,
      );
    } on _StudentRevisionConflictException {
      await _refreshStudentsFromServerFor(therapistId);
      return StudentWriteResult(
        committed: false,
        queuedForRetry: false,
        studentId: studentId,
        statusMessage:
            'Delete conflict detected. Local view refreshed from server.',
        pendingWrites: _pendingWrites.length,
      );
    } catch (e) {
      final operation = StudentPendingOperation.delete(
        operationId: _nextOperationId(),
        studentId: studentId,
        expectedRevision: existing?.revision,
      );
      await _queuePendingOperation(operation, therapistId);
      return StudentWriteResult(
        committed: false,
        queuedForRetry: true,
        studentId: studentId,
        statusMessage: 'Delete failed remotely. Added to pending queue.',
        pendingWrites: _pendingWrites.length,
      );
    }
  }

  static Future<Student?> getStudent(String studentId) async {
    final therapistId = _currentTherapistId;
    if (therapistId == null) {
      return null;
    }

    await _ensureBootstrap(therapistId);

    final fromCache = _findCachedStudent(studentId);
    if (fromCache != null) {
      return fromCache;
    }

    final doc = await _studentsCollection.doc(studentId).get();
    if (!doc.exists) {
      return null;
    }

    final student = Student.fromFirestore(doc);
    await _commitLocalStudentUpsert(student, therapistId);
    return student;
  }

  static Future<StudentReconciliationReport> reconcilePendingWrites() async {
    return _reconcilePendingWritesInternal(hydrateRemote: true);
  }

  static Future<StudentReconciliationReport> _reconcilePendingWritesInternal({
    required bool hydrateRemote,
  }) async {
    final therapistId = _currentTherapistId;
    if (therapistId == null) {
      return StudentReconciliationReport(
        pendingBefore: 0,
        processed: 0,
        failed: 0,
        pendingAfter: 0,
        deferred: 0,
        remoteHydrated: false,
        performedAtUtc: DateTime.now().toUtc(),
      );
    }

    await _ensureBootstrap(therapistId);

    if (_isReconciling) {
      return StudentReconciliationReport(
        pendingBefore: _pendingWrites.length,
        processed: 0,
        failed: 0,
        pendingAfter: _pendingWrites.length,
        deferred: _pendingWrites.length,
        remoteHydrated: false,
        performedAtUtc: DateTime.now().toUtc(),
      );
    }

    _isReconciling = true;
    try {
      final pendingSnapshot = List<StudentPendingOperation>.from(_pendingWrites);
      var processed = 0;
      var failed = 0;
      var deferred = 0;
      var hadConflict = false;

      for (final operation in pendingSnapshot) {
        final nowUtc = DateTime.now().toUtc();
        if (!operation.isReadyForRetry(nowUtc)) {
          deferred += 1;
          continue;
        }

        try {
          await _applyPendingOperation(operation, therapistId);
          _pendingWrites.removeWhere(
            (item) => item.operationId == operation.operationId,
          );
          processed += 1;
        } on _StudentRevisionConflictException {
          _pendingWrites.removeWhere(
            (item) => item.operationId == operation.operationId,
          );
          hadConflict = true;
          failed += 1;
        } catch (e) {
          _replacePendingWithRetry(operation, '$e');
          failed += 1;
        }
      }

      await _persistPendingWrites(therapistId);
      _emitPendingWrites();
      final shouldHydrate = hydrateRemote || hadConflict;
      final remoteHydrated =
          shouldHydrate ? await _refreshStudentsFromServerFor(therapistId) : false;

      return StudentReconciliationReport(
        pendingBefore: pendingSnapshot.length,
        processed: processed,
        failed: failed,
        pendingAfter: _pendingWrites.length,
        deferred: deferred,
        remoteHydrated: remoteHydrated,
        performedAtUtc: DateTime.now().toUtc(),
      );
    } finally {
      _isReconciling = false;
    }
  }

  static Future<void> refreshStudentsFromServer() async {
    final therapistId = _currentTherapistId;
    if (therapistId == null) {
      return;
    }

    await _ensureBootstrap(therapistId);
    await _refreshStudentsFromServerFor(therapistId);
  }

  static Future<void> _ensureBootstrap(String therapistId) {
    if (_activeTherapistId == therapistId && _bootstrapFuture != null) {
      return _bootstrapFuture!;
    }

    _bootstrapFuture = _bootstrapForTherapist(therapistId);
    return _bootstrapFuture!;
  }

  static Future<void> _bootstrapForTherapist(String therapistId) async {
    _autoReconcileTimer?.cancel();
    _autoReconcileTimer = null;
    await _studentsSubscription?.cancel();
    _studentsSubscription = null;
    await _studentBindingsSubscription?.cancel();
    _studentBindingsSubscription = null;

    _activeTherapistId = therapistId;
    _hasReceivedInitialRemoteSnapshot = false;
    _sharedStudentIds = <String>{};
    _cachedStudents = await _readCachedStudents(therapistId);
    _pendingWrites = await _readPendingWrites(therapistId);

    _sortCachedStudents();
    _emitStudents();
    _emitPendingWrites();
    _startRemoteSync(therapistId);
    _startSharedBindingsSync(therapistId);
    _startAutoReconcile(therapistId);
  }

  static void _startRemoteSync(String therapistId) {
    _studentsSubscription = _studentsCollection
        .where('therapistId', isEqualTo: therapistId)
        .snapshots()
        .listen(
      (snapshot) {
        if (_activeTherapistId != therapistId) {
          return;
        }

        _applyRemoteSnapshot(snapshot);
        unawaited(_persistCachedStudents(therapistId));
      },
      onError: (Object error, StackTrace stackTrace) {
        print('[StudentService] ⚠️ Remote student stream error: $error');
      },
    );

    unawaited(_refreshStudentsFromServerFor(therapistId));
  }

  static void _startSharedBindingsSync(String accountId) {
    _studentBindingsSubscription = _studentAccessBindingsCollection
        .where('accountId', isEqualTo: accountId)
        .where('status', isEqualTo: StudentAccessBindingStatus.active.wireValue)
        .snapshots()
        .listen(
      (snapshot) {
        if (_activeTherapistId != accountId) {
          return;
        }

        _sharedStudentIds = _extractActiveSharedStudentIds(snapshot);
        unawaited(_refreshStudentsFromServerFor(accountId));
      },
      onError: (Object error, StackTrace stackTrace) {
        print('[StudentService] ⚠️ Shared binding stream error: $error');
      },
    );
  }

  static void _startAutoReconcile(String therapistId) {
    _autoReconcileTimer?.cancel();
    _autoReconcileTimer = Timer.periodic(_autoReconcileInterval, (_) {
      if (_activeTherapistId != therapistId) {
        return;
      }

      if (_pendingWrites.isEmpty || _isReconciling) {
        return;
      }

      unawaited(_runAutoReconcileTick(therapistId));
    });
  }

  static Future<void> _runAutoReconcileTick(String therapistId) async {
    if (_activeTherapistId != therapistId) {
      return;
    }

    if (_currentTherapistId != therapistId) {
      return;
    }

    final report = await _reconcilePendingWritesInternal(hydrateRemote: false);
    if (report.processed > 0 || report.failed > 0) {
      print(
        '[StudentService] 🔁 Auto-reconcile processed=${report.processed}, failed=${report.failed}, deferred=${report.deferred}, pending=${report.pendingAfter}',
      );
    }
  }

  static void _applyRemoteSnapshot(
    QuerySnapshot<Map<String, dynamic>> snapshot,
  ) {
    if (!_hasReceivedInitialRemoteSnapshot) {
      _cachedStudents = <Student>[];
      _hasReceivedInitialRemoteSnapshot = true;
    }

    for (final change in snapshot.docChanges) {
      final studentId = change.doc.id;

      if (change.type == DocumentChangeType.removed) {
        _cachedStudents.removeWhere((item) => item.id == studentId);
        continue;
      }

      final mapped = Student.fromFirestore(change.doc);
      final index = _cachedStudents.indexWhere((item) => item.id == mapped.id);
      if (index == -1) {
        _cachedStudents.add(mapped);
      } else {
        _cachedStudents[index] = mapped;
      }
    }

    _sortCachedStudents();
    _emitStudents();
  }

  static Future<Student> _updateStudentRemoteWithConflictGuard({
    required String studentId,
    required String firstName,
    required String lastName,
    required String? notes,
    required int? expectedRevision,
  }) async {
    final nowUtc = DateTime.now().toUtc();
    return FirebaseService.firestore.runTransaction((transaction) async {
      final docRef = _studentsCollection.doc(studentId);
      final snapshot = await transaction.get(docRef);
      if (!snapshot.exists) {
        throw StateError('Student does not exist: $studentId');
      }

      final remoteStudent = Student.fromFirestore(snapshot);
      final remoteRevision = remoteStudent.revision;
      if (expectedRevision != null && remoteRevision != expectedRevision) {
        throw _StudentRevisionConflictException(
          studentId: studentId,
          expectedRevision: expectedRevision,
          remoteRevision: remoteRevision,
        );
      }

      final nextRevision = remoteRevision + 1;
      transaction.update(docRef, <String, dynamic>{
        'firstName': firstName,
        'lastName': lastName,
        'notes': notes,
        'updatedAt': Timestamp.fromDate(nowUtc),
        'revision': nextRevision,
      });

      return remoteStudent.copyWith(
        firstName: firstName,
        lastName: lastName,
        notes: notes,
        updatedAt: nowUtc,
        revision: nextRevision,
      );
    });
  }

  static Future<void> _deleteStudentRemoteWithConflictGuard({
    required String studentId,
    required int? expectedRevision,
  }) async {
    await FirebaseService.firestore.runTransaction((transaction) async {
      final docRef = _studentsCollection.doc(studentId);
      final snapshot = await transaction.get(docRef);
      if (!snapshot.exists) {
        return;
      }

      if (expectedRevision != null) {
        final remoteStudent = Student.fromFirestore(snapshot);
        if (remoteStudent.revision != expectedRevision) {
          throw _StudentRevisionConflictException(
            studentId: studentId,
            expectedRevision: expectedRevision,
            remoteRevision: remoteStudent.revision,
          );
        }
      }

      transaction.delete(docRef);
    });
  }

  static Future<void> _queuePendingOperation(
    StudentPendingOperation operation,
    String therapistId,
  ) async {
    if (operation.type == StudentPendingOperationType.update) {
      _pendingWrites.removeWhere(
        (item) =>
            item.studentId == operation.studentId &&
            item.type == StudentPendingOperationType.update,
      );
    }

    if (operation.type == StudentPendingOperationType.delete) {
      _pendingWrites.removeWhere(
        (item) =>
            item.studentId == operation.studentId &&
            item.type != StudentPendingOperationType.delete,
      );
    }

    _pendingWrites.add(operation);
    await _persistPendingWrites(therapistId);
    _emitPendingWrites();
  }

  static String _buildUpdateFingerprint({
    required String firstName,
    required String lastName,
    required String? notes,
  }) {
    return '${firstName.trim()}|${lastName.trim()}|${notes?.trim() ?? ''}';
  }

  static bool _isDuplicateUpdateInCooldown(
    String studentId,
    String fingerprint,
  ) {
    final lastFingerprint = _lastUpdateFingerprintByStudent[studentId];
    final lastUpdateAt = _lastUpdateAtByStudent[studentId];
    if (lastFingerprint == null || lastUpdateAt == null) {
      return false;
    }

    final elapsed = DateTime.now().toUtc().difference(lastUpdateAt);
    return elapsed <= _duplicateUpdateCooldown && lastFingerprint == fingerprint;
  }

  static void _markUpdateFingerprint(String studentId, String fingerprint) {
    _lastUpdateFingerprintByStudent[studentId] = fingerprint;
    _lastUpdateAtByStudent[studentId] = DateTime.now().toUtc();
  }

  static Future<void> _applyPendingOperation(
    StudentPendingOperation operation,
    String therapistId,
  ) async {
    switch (operation.type) {
      case StudentPendingOperationType.create:
        final student = operation.studentPayload;
        if (student == null) {
          throw StateError('Missing student payload for create operation');
        }
        await FirebaseService.firestore.runTransaction((transaction) async {
          final docRef = _studentsCollection.doc(student.id);
          final snapshot = await transaction.get(docRef);
          if (snapshot.exists) {
            final remoteStudent = Student.fromFirestore(snapshot);
            throw _StudentRevisionConflictException(
              studentId: student.id,
              expectedRevision: operation.expectedRevision ?? 0,
              remoteRevision: remoteStudent.revision,
            );
          }
          transaction.set(docRef, student.toFirestore());
        });
        await _commitLocalStudentUpsert(student, therapistId);
        break;
      case StudentPendingOperationType.update:
        final student = operation.studentPayload;
        if (student == null) {
          throw StateError('Missing student payload for update operation');
        }
        final updatedStudent = await _updateStudentRemoteWithConflictGuard(
          studentId: student.id,
          firstName: student.firstName,
          lastName: student.lastName,
          notes: student.notes,
          expectedRevision: operation.expectedRevision ?? student.revision,
        );
        await _commitLocalStudentUpsert(updatedStudent, therapistId);
        break;
      case StudentPendingOperationType.delete:
        await _deleteStudentRemoteWithConflictGuard(
          studentId: operation.studentId,
          expectedRevision: operation.expectedRevision,
        );
        await _commitLocalStudentDelete(operation.studentId, therapistId);
        break;
    }
  }

  static void _replacePendingWithRetry(
    StudentPendingOperation operation,
    String errorMessage,
  ) {
    final index = _pendingWrites.indexWhere(
      (item) => item.operationId == operation.operationId,
    );
    if (index == -1) {
      return;
    }

    _pendingWrites[index] = operation.withRetryError(errorMessage);
  }

  static Future<void> _commitLocalStudentUpsert(
    Student student,
    String therapistId,
  ) async {
    final index = _cachedStudents.indexWhere((item) => item.id == student.id);
    if (index == -1) {
      _cachedStudents.add(student);
    } else {
      _cachedStudents[index] = student;
    }

    _sortCachedStudents();
    _emitStudents();
    await _persistCachedStudents(therapistId);
  }

  static Future<void> _commitLocalStudentDelete(
    String studentId,
    String therapistId,
  ) async {
    _cachedStudents.removeWhere((item) => item.id == studentId);
    _emitStudents();
    await _persistCachedStudents(therapistId);
  }

  static Future<bool> _refreshStudentsFromServerFor(String therapistId) async {
    try {
      final ownSnapshot = await _studentsCollection
          .where('therapistId', isEqualTo: therapistId)
          .get();

      if (_activeTherapistId != therapistId) {
        return false;
      }

      final mergedById = <String, Student>{
        for (final doc in ownSnapshot.docs) doc.id: Student.fromFirestore(doc),
      };

      if (_sharedStudentIds.isNotEmpty) {
        final sharedStudents = await _fetchStudentsByIds(_sharedStudentIds);
        for (final student in sharedStudents) {
          mergedById[student.id] = student;
        }
      }

      _cachedStudents = mergedById.values.toList();
      _sortCachedStudents();
      _emitStudents();
      await _persistCachedStudents(therapistId);
      return true;
    } catch (e) {
      print('[StudentService] ⚠️ Refresh from server failed: $e');
      return false;
    }
  }

  static Future<List<Student>> _fetchStudentsByIds(Set<String> studentIds) async {
    if (studentIds.isEmpty) {
      return <Student>[];
    }

    final allIds = studentIds.toList(growable: false);
    final allStudents = <Student>[];
    const chunkSize = 10;

    for (var i = 0; i < allIds.length; i += chunkSize) {
      final end = (i + chunkSize > allIds.length) ? allIds.length : i + chunkSize;
      final chunk = allIds.sublist(i, end);
      final snapshot = await _studentsCollection
          .where(FieldPath.documentId, whereIn: chunk)
          .get();
      allStudents.addAll(snapshot.docs.map(Student.fromFirestore));
    }

    return allStudents;
  }

  static Set<String> _extractActiveSharedStudentIds(
    QuerySnapshot<Map<String, dynamic>> snapshot,
  ) {
    final nowUtc = DateTime.now().toUtc();
    final studentIds = <String>{};
    for (final doc in snapshot.docs) {
      final binding = StudentAccessBinding.fromFirestore(doc);
      if (binding.isActiveAt(nowUtc) && binding.studentId.trim().isNotEmpty) {
        studentIds.add(binding.studentId);
      }
    }
    return studentIds;
  }

  static Future<List<Student>> _readCachedStudents(String therapistId) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final encoded = prefs.getString(_studentsCacheKey(therapistId));
      if (encoded == null || encoded.isEmpty) {
        return <Student>[];
      }

      final decoded = jsonDecode(encoded);
      if (decoded is! List) {
        return <Student>[];
      }

      return decoded
          .whereType<Map>()
          .map((entry) => Student.fromJson(Map<String, dynamic>.from(entry)))
          .toList();
    } catch (e) {
      print('[StudentService] ⚠️ Unable to read local students cache: $e');
      return <Student>[];
    }
  }

  static Future<List<StudentPendingOperation>> _readPendingWrites(
    String therapistId,
  ) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final encoded = prefs.getString(_pendingWritesKey(therapistId));
      if (encoded == null || encoded.isEmpty) {
        return <StudentPendingOperation>[];
      }

      final decoded = jsonDecode(encoded);
      if (decoded is! List) {
        return <StudentPendingOperation>[];
      }

      return decoded
          .whereType<Map>()
          .map(
            (entry) => StudentPendingOperation.fromJson(
              Map<String, dynamic>.from(entry),
            ),
          )
          .toList();
    } catch (e) {
      print('[StudentService] ⚠️ Unable to read pending writes cache: $e');
      return <StudentPendingOperation>[];
    }
  }

  static Future<void> _persistCachedStudents(String therapistId) async {
    final prefs = await SharedPreferences.getInstance();
    final jsonPayload =
        jsonEncode(_cachedStudents.map((item) => item.toJson()).toList());
    await prefs.setString(_studentsCacheKey(therapistId), jsonPayload);
  }

  static Future<void> _persistPendingWrites(String therapistId) async {
    final prefs = await SharedPreferences.getInstance();
    final jsonPayload =
        jsonEncode(_pendingWrites.map((item) => item.toJson()).toList());
    await prefs.setString(_pendingWritesKey(therapistId), jsonPayload);
  }

  static void _emitStudents() {
    _studentsController.add(List<Student>.unmodifiable(_cachedStudents));
  }

  static void _emitPendingWrites() {
    _pendingWritesController.add(_pendingWrites.length);
  }

  static void _sortCachedStudents() {
    _cachedStudents.sort(
      (a, b) => a.fullName.toLowerCase().compareTo(b.fullName.toLowerCase()),
    );
  }

  static Student? _findCachedStudent(String studentId) {
    for (final student in _cachedStudents) {
      if (student.id == studentId) {
        return student;
      }
    }
    return null;
  }

  static String _studentsCacheKey(String therapistId) =>
      'students_cache_v1_$therapistId';

  static String _pendingWritesKey(String therapistId) =>
      'students_pending_writes_v1_$therapistId';

  static String _nextOperationId() {
    _operationCounter += 1;
    return 'student-op-${DateTime.now().toUtc().microsecondsSinceEpoch}-$_operationCounter';
  }

  static Future<void> clearSessionState() async {
    _autoReconcileTimer?.cancel();
    _autoReconcileTimer = null;
    await _studentsSubscription?.cancel();
    _studentsSubscription = null;
    await _studentBindingsSubscription?.cancel();
    _studentBindingsSubscription = null;

    _activeTherapistId = null;
    _bootstrapFuture = null;
    _cachedStudents = <Student>[];
    _pendingWrites = <StudentPendingOperation>[];
    _sharedStudentIds = <String>{};
    _isReconciling = false;
    _hasReceivedInitialRemoteSnapshot = false;
    _lastUpdateFingerprintByStudent.clear();
    _lastUpdateAtByStudent.clear();
    _emitStudents();
    _emitPendingWrites();
  }
}

class _StudentRevisionConflictException implements Exception {
  final String studentId;
  final int expectedRevision;
  final int remoteRevision;

  _StudentRevisionConflictException({
    required this.studentId,
    required this.expectedRevision,
    required this.remoteRevision,
  });

  @override
  String toString() {
    return 'Student revision conflict for $studentId (expected=$expectedRevision, remote=$remoteRevision)';
  }
}
