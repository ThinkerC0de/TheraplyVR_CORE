import 'package:flutter_controller/models/student.dart';

enum StudentPendingOperationType {
  create,
  update,
  delete,
}

extension StudentPendingOperationTypeCodec on StudentPendingOperationType {
  static StudentPendingOperationType fromWire(String? wireValue) {
    switch (wireValue) {
      case 'create':
        return StudentPendingOperationType.create;
      case 'update':
        return StudentPendingOperationType.update;
      case 'delete':
        return StudentPendingOperationType.delete;
      default:
        return StudentPendingOperationType.update;
    }
  }

  String get wireValue {
    switch (this) {
      case StudentPendingOperationType.create:
        return 'create';
      case StudentPendingOperationType.update:
        return 'update';
      case StudentPendingOperationType.delete:
        return 'delete';
    }
  }
}

class StudentPendingOperation {
  final String operationId;
  final StudentPendingOperationType type;
  final String studentId;
  final Student? studentPayload;
  final DateTime queuedAtUtc;
  final DateTime nextAttemptAtUtc;
  final int? expectedRevision;
  final int retryCount;
  final String? lastError;

  const StudentPendingOperation({
    required this.operationId,
    required this.type,
    required this.studentId,
    required this.studentPayload,
    required this.queuedAtUtc,
    required this.nextAttemptAtUtc,
    this.expectedRevision,
    required this.retryCount,
    this.lastError,
  });

  factory StudentPendingOperation.create({
    required String operationId,
    required Student student,
    int? expectedRevision,
  }) {
    return StudentPendingOperation(
      operationId: operationId,
      type: StudentPendingOperationType.create,
      studentId: student.id,
      studentPayload: student,
      queuedAtUtc: DateTime.now().toUtc(),
      nextAttemptAtUtc: DateTime.now().toUtc(),
      expectedRevision: expectedRevision,
      retryCount: 0,
    );
  }

  factory StudentPendingOperation.update({
    required String operationId,
    required Student student,
    int? expectedRevision,
  }) {
    return StudentPendingOperation(
      operationId: operationId,
      type: StudentPendingOperationType.update,
      studentId: student.id,
      studentPayload: student,
      queuedAtUtc: DateTime.now().toUtc(),
      nextAttemptAtUtc: DateTime.now().toUtc(),
      expectedRevision: expectedRevision,
      retryCount: 0,
    );
  }

  factory StudentPendingOperation.delete({
    required String operationId,
    required String studentId,
    int? expectedRevision,
  }) {
    return StudentPendingOperation(
      operationId: operationId,
      type: StudentPendingOperationType.delete,
      studentId: studentId,
      studentPayload: null,
      queuedAtUtc: DateTime.now().toUtc(),
      nextAttemptAtUtc: DateTime.now().toUtc(),
      expectedRevision: expectedRevision,
      retryCount: 0,
    );
  }

  factory StudentPendingOperation.fromJson(Map<String, dynamic> json) {
    return StudentPendingOperation(
      operationId: json['operationId'] as String? ?? '',
      type: StudentPendingOperationTypeCodec.fromWire(
        json['type'] as String?,
      ),
      studentId: json['studentId'] as String? ?? '',
      studentPayload: json['studentPayload'] is Map<String, dynamic>
          ? Student.fromJson(json['studentPayload'] as Map<String, dynamic>)
          : null,
      queuedAtUtc: DateTime.tryParse(json['queuedAtUtc'] as String? ?? '')
              ?.toUtc() ??
          DateTime.fromMillisecondsSinceEpoch(0, isUtc: true),
      nextAttemptAtUtc:
          DateTime.tryParse(json['nextAttemptAtUtc'] as String? ?? '')
                  ?.toUtc() ??
              DateTime.fromMillisecondsSinceEpoch(0, isUtc: true),
      expectedRevision: (json['expectedRevision'] as num?)?.toInt(),
      retryCount: json['retryCount'] as int? ?? 0,
      lastError: json['lastError'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return <String, dynamic>{
      'operationId': operationId,
      'type': type.wireValue,
      'studentId': studentId,
      'studentPayload': studentPayload?.toJson(),
      'queuedAtUtc': queuedAtUtc.toUtc().toIso8601String(),
      'nextAttemptAtUtc': nextAttemptAtUtc.toUtc().toIso8601String(),
      'expectedRevision': expectedRevision,
      'retryCount': retryCount,
      'lastError': lastError,
    };
  }

  StudentPendingOperation withRetryError(String errorMessage) {
    return StudentPendingOperation(
      operationId: operationId,
      type: type,
      studentId: studentId,
      studentPayload: studentPayload,
      queuedAtUtc: queuedAtUtc,
      nextAttemptAtUtc: DateTime.now()
          .toUtc()
          .add(Duration(seconds: _backoffSeconds(retryCount + 1))),
      expectedRevision: expectedRevision,
      retryCount: retryCount + 1,
      lastError: errorMessage,
    );
  }

  bool isReadyForRetry(DateTime atUtc) =>
      !atUtc.toUtc().isBefore(nextAttemptAtUtc);

  static int _backoffSeconds(int nextRetryCount) {
    if (nextRetryCount <= 1) {
      return 5;
    }

    final cappedRetryCount = nextRetryCount > 6 ? 6 : nextRetryCount;
    final exponent = cappedRetryCount - 1;
    final delay = 5 * (1 << exponent);
    return delay > 300 ? 300 : delay;
  }
}

class StudentWriteResult {
  final bool committed;
  final bool queuedForRetry;
  final String studentId;
  final String statusMessage;
  final int pendingWrites;

  const StudentWriteResult({
    required this.committed,
    required this.queuedForRetry,
    required this.studentId,
    required this.statusMessage,
    required this.pendingWrites,
  });
}

class StudentReconciliationReport {
  final int pendingBefore;
  final int processed;
  final int failed;
  final int pendingAfter;
  final int deferred;
  final bool remoteHydrated;
  final DateTime performedAtUtc;

  const StudentReconciliationReport({
    required this.pendingBefore,
    required this.processed,
    required this.failed,
    required this.pendingAfter,
    required this.deferred,
    required this.remoteHydrated,
    required this.performedAtUtc,
  });
}
