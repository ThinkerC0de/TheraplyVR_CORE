import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_controller/models/guided_session_plan.dart';
import 'package:flutter_controller/models/entitlement_access.dart';
import 'package:flutter_controller/models/ops_error_catalog.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/student.dart';
import 'package:flutter_controller/models/student_roster_sync.dart';
import 'package:flutter_controller/models/therapist_session_settings.dart';
import 'package:flutter_controller/models/therapy_session_record.dart';
import 'package:flutter_controller/services/student_service.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:flutter_controller/services/entitlement_service.dart';
import 'package:flutter_controller/services/session_journal_service.dart';
import 'package:flutter_controller/services/therapist_session_settings_service.dart';
import 'package:flutter_controller/services/operator_incident_popup_queue.dart';
import 'package:flutter_controller/screens/login_screen.dart';
import 'package:flutter_controller/screens/scanner_screen.dart';

class StudentsScreen extends StatefulWidget {
  const StudentsScreen({super.key});

  @override
  State<StudentsScreen> createState() => _StudentsScreenState();
}

class _StudentsScreenState extends State<StudentsScreen>
    with WidgetsBindingObserver {
  String? _selectedStudentId;
  TherapistSessionSettings _therapistSessionSettings =
      TherapistSessionSettings.defaults();
  late final OperatorIncidentPopupQueue _incidentPopupQueue;
  bool _therapistSettingsLoading = true;
  bool _activeSessionBannerLoading = true;
  bool _activeSessionEndInFlight = false;
  TherapySessionRecord? _latestActiveSession;
  Student? _latestActiveSessionStudent;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _incidentPopupQueue = OperatorIncidentPopupQueue(
      queueName: 'students_screen',
      languageResolver: () => _therapistSessionSettings.operatorUiLanguage,
    );
    unawaited(_loadTherapistSessionSettings());
    unawaited(_refreshActiveSessionBanner());
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      unawaited(_refreshActiveSessionBanner());
    }
  }

  @override
  Widget build(BuildContext context) {
    final canManageStudents =
        EntitlementService.activeAccess?.role == EntitlementRole.therapist;
    final currentUser = FirebaseService.currentUser;
    final accountLabel = currentUser?.email ?? currentUser?.uid ?? 'unknown';
    final role =
        EntitlementService.activeAccess?.role ?? EntitlementRole.unknown;
    final roleLabel = role.wireValue;
    final isReadOnlyRole = role != EntitlementRole.therapist;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Session Setup'),
        actions: [
          if (canManageStudents)
            IconButton(
              icon: _therapistSettingsLoading
                  ? const SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.settings),
              onPressed: _therapistSettingsLoading
                  ? null
                  : _openTherapistSettingsDialog,
              tooltip: 'Session settings',
            ),
          IconButton(
            icon: const Icon(Icons.logout),
            onPressed: _handleLogout,
            tooltip: 'Log out therapist',
          ),
        ],
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 10, 12, 8),
            child: Card(
              margin: EdgeInsets.zero,
              child: ExpansionTile(
                tilePadding: const EdgeInsets.symmetric(
                  horizontal: 12,
                  vertical: 2,
                ),
                childrenPadding: const EdgeInsets.fromLTRB(12, 0, 12, 12),
                leading: const Icon(Icons.badge_outlined),
                title: Text(
                  accountLabel,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(fontWeight: FontWeight.w700),
                ),
                subtitle: Text(
                  isReadOnlyRole
                      ? 'Role: $roleLabel (read-only)'
                      : 'Role: $roleLabel',
                ),
                children: [
                  Row(
                    children: [
                      Icon(
                        isReadOnlyRole ? Icons.visibility : Icons.verified_user,
                        size: 16,
                        color: isReadOnlyRole
                            ? Colors.orange.shade700
                            : Colors.green.shade700,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          isReadOnlyRole
                              ? 'Read-only mode. Therapist settings and roster editing are disabled.'
                              : 'Configure therapist settings and open student sessions.',
                          style: TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w600,
                            color: isReadOnlyRole
                                ? Colors.orange.shade800
                                : Colors.green.shade800,
                          ),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  Align(
                    alignment: Alignment.centerLeft,
                    child: TextButton.icon(
                      onPressed: _runReconciliation,
                      icon: const Icon(Icons.sync, size: 16),
                      label: const Text('Refresh and reconcile'),
                    ),
                  ),
                ],
              ),
            ),
          ),
          if (_activeSessionBannerLoading || _latestActiveSession != null)
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 0, 12, 8),
              child: _buildActiveSessionBanner(),
            ),
          StreamBuilder<int>(
            stream: StudentService.watchPendingWritesCount(),
            builder: (context, snapshot) {
              final pendingWrites = snapshot.data ?? 0;
              if (pendingWrites <= 0) {
                return const SizedBox.shrink();
              }

              return Container(
                width: double.infinity,
                margin: const EdgeInsets.fromLTRB(12, 0, 12, 8),
                padding:
                    const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                decoration: BoxDecoration(
                  color: Colors.amber.shade50,
                  borderRadius: BorderRadius.circular(10),
                  border: Border.all(color: Colors.amber.shade200),
                ),
                child: Row(
                  children: [
                    Icon(Icons.sync_problem,
                        size: 16, color: Colors.amber.shade900),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        '$pendingWrites pending write(s). Tap sync to reconcile.',
                        style: TextStyle(
                          color: Colors.amber.shade900,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                  ],
                ),
              );
            },
          ),
          Expanded(
            child: StreamBuilder<List<Student>>(
              stream: StudentService.getStudents(),
              builder: (context, snapshot) {
                if (snapshot.hasError) {
                  final error = snapshot.error!;
                  final likelyOffline = _isLikelyOfflineError(error);
                  final title = likelyOffline
                      ? 'Offline data state'
                      : 'Roster load failed';
                  final subtitle = likelyOffline
                      ? 'Network seems unavailable. You can retry or reconcile pending writes.'
                      : 'Unable to load students. Retry sync or check backend health.';

                  return Center(
                    child: Padding(
                      padding: const EdgeInsets.all(20),
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Icon(
                            likelyOffline
                                ? Icons.wifi_off
                                : Icons.error_outline,
                            size: 64,
                            color: likelyOffline
                                ? Colors.orange.shade700
                                : Colors.red.shade700,
                          ),
                          const SizedBox(height: 14),
                          Text(
                            title,
                            style: const TextStyle(
                              fontSize: 18,
                              fontWeight: FontWeight.w700,
                            ),
                            textAlign: TextAlign.center,
                          ),
                          const SizedBox(height: 8),
                          Text(
                            subtitle,
                            style: TextStyle(color: Colors.grey.shade700),
                            textAlign: TextAlign.center,
                          ),
                          const SizedBox(height: 8),
                          Text(
                            '$error',
                            style: TextStyle(
                              color: Colors.grey.shade600,
                              fontSize: 12,
                            ),
                            textAlign: TextAlign.center,
                          ),
                          const SizedBox(height: 14),
                          Wrap(
                            spacing: 8,
                            runSpacing: 8,
                            alignment: WrapAlignment.center,
                            children: [
                              OutlinedButton.icon(
                                onPressed: _refreshFromServer,
                                icon: const Icon(Icons.refresh),
                                label: const Text('Retry'),
                              ),
                              OutlinedButton.icon(
                                onPressed: _runReconciliation,
                                icon: const Icon(Icons.sync),
                                label: const Text('Reconcile'),
                              ),
                              if (canManageStudents)
                                ElevatedButton.icon(
                                  onPressed: () => _showStudentDialog(context),
                                  icon: const Icon(Icons.person_add),
                                  label: const Text('Add student'),
                                ),
                            ],
                          ),
                        ],
                      ),
                    ),
                  );
                }

                if (!snapshot.hasData) {
                  return const Center(child: CircularProgressIndicator());
                }

                final students = snapshot.data!;

                if (students.isEmpty) {
                  return Center(
                    child: Padding(
                      padding: const EdgeInsets.all(20),
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Icon(Icons.school_outlined,
                              size: 64, color: Colors.grey[400]),
                          const SizedBox(height: 16),
                          const Text(
                            'No students in roster',
                            style: TextStyle(
                              fontSize: 18,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          const SizedBox(height: 8),
                          Text(
                            canManageStudents
                                ? 'Add the first student to start device control flow.'
                                : 'Read-only role: wait for therapist roster updates.',
                            style: TextStyle(color: Colors.grey[600]),
                            textAlign: TextAlign.center,
                          ),
                          if (canManageStudents) ...[
                            const SizedBox(height: 16),
                            ElevatedButton.icon(
                              onPressed: () => _showStudentDialog(context),
                              icon: const Icon(Icons.person_add),
                              label: const Text('Add first student'),
                            ),
                          ],
                        ],
                      ),
                    ),
                  );
                }

                return RefreshIndicator(
                  onRefresh: _refreshFromServer,
                  child: ListView.separated(
                    physics: const AlwaysScrollableScrollPhysics(),
                    itemCount: students.length + 1,
                    separatorBuilder: (_, __) => const SizedBox(height: 0),
                    itemBuilder: (context, index) {
                      if (index == 0) {
                        return Padding(
                          padding: const EdgeInsets.fromLTRB(16, 4, 16, 8),
                          child: Row(
                            children: [
                              const Text(
                                'Student roster',
                                style: TextStyle(
                                  fontSize: 15,
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                              const SizedBox(width: 8),
                              Container(
                                padding: const EdgeInsets.symmetric(
                                    horizontal: 8, vertical: 4),
                                decoration: BoxDecoration(
                                  color: Colors.blue.shade50,
                                  borderRadius: BorderRadius.circular(999),
                                  border:
                                      Border.all(color: Colors.blue.shade100),
                                ),
                                child: Text(
                                  '${students.length}',
                                  style: TextStyle(
                                    color: Colors.blue.shade800,
                                    fontWeight: FontWeight.w700,
                                    fontSize: 11,
                                  ),
                                ),
                              ),
                              const Spacer(),
                              IconButton(
                                icon: Icon(
                                  Icons.sync,
                                  size: 18,
                                  color: Colors.grey.shade700,
                                ),
                                tooltip: 'Refresh and reconcile',
                                onPressed: _runReconciliation,
                              ),
                            ],
                          ),
                        );
                      }

                      final student = students[index - 1];
                      return _buildStudentCard(
                        student,
                        isSelected: student.id == _selectedStudentId,
                      );
                    },
                  ),
                );
              },
            ),
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: canManageStudents ? () => _showStudentDialog(context) : null,
        tooltip: 'Add Student',
        icon: const Icon(Icons.add),
        label: const Text('Add'),
      ),
    );
  }

  Future<void> _refreshActiveSessionBanner() async {
    final therapistId = FirebaseService.currentUser?.uid.trim() ?? '';
    if (therapistId.isEmpty) {
      if (!mounted) {
        return;
      }
      setState(() {
        _activeSessionBannerLoading = false;
        _latestActiveSession = null;
        _latestActiveSessionStudent = null;
      });
      return;
    }

    if (mounted) {
      setState(() {
        _activeSessionBannerLoading = true;
      });
    }

    try {
      final session =
          await SessionJournalService.fetchLatestUnfinishedForTherapist(
        therapistId: therapistId,
      );
      Student? student;
      if (session != null && session.studentId.trim().isNotEmpty) {
        student = await StudentService.getStudent(session.studentId);
      }

      if (!mounted) {
        return;
      }

      setState(() {
        _latestActiveSession = session;
        _latestActiveSessionStudent = student;
        _activeSessionBannerLoading = false;
      });
    } catch (e) {
      if (!mounted) {
        return;
      }

      setState(() {
        _activeSessionBannerLoading = false;
      });
      _enqueueIncidentAlert(
        title: 'Active session banner refresh failed',
        message: '$e',
        reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ?? 'UNSPECIFIED',
        severity: OperatorIncidentSeverity.warning,
      );
    }
  }

  Widget _buildActiveSessionBanner() {
    if (_activeSessionBannerLoading) {
      return Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        decoration: BoxDecoration(
          color: Colors.blueGrey.shade50,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(color: Colors.blueGrey.shade100),
        ),
        child: Row(
          children: [
            const SizedBox(
              width: 16,
              height: 16,
              child: CircularProgressIndicator(strokeWidth: 2),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                'Checking active session state...',
                style: TextStyle(
                  color: Colors.blueGrey.shade800,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          ],
        ),
      );
    }

    final session = _latestActiveSession;
    if (session == null) {
      return const SizedBox.shrink();
    }

    final studentLabel =
        _latestActiveSessionStudent?.fullName.trim().isNotEmpty == true
            ? _latestActiveSessionStudent!.fullName
            : session.studentId;
    final stateLabel = _formatSessionState(session);
    final latestGameId = session.latestGameId.trim();
    final hasGameLabel = latestGameId.isNotEmpty;

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.orange.shade50,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: Colors.orange.shade200),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.warning_amber_rounded, color: Colors.orange.shade800),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  'Active session detected',
                  style: TextStyle(
                    color: Colors.orange.shade900,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
              IconButton(
                onPressed: _activeSessionEndInFlight
                    ? null
                    : () => unawaited(_refreshActiveSessionBanner()),
                icon: const Icon(Icons.refresh, size: 18),
                tooltip: 'Refresh active session',
              ),
            ],
          ),
          const SizedBox(height: 4),
          Text(
            'You have an active session with $studentLabel.',
            style: TextStyle(
              color: Colors.orange.shade900,
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            hasGameLabel
                ? 'State: $stateLabel | Game: $latestGameId'
                : 'State: $stateLabel',
            style: TextStyle(
              color: Colors.orange.shade900,
              fontSize: 12,
            ),
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              OutlinedButton.icon(
                onPressed: _activeSessionEndInFlight
                    ? null
                    : _openActiveSessionFromBanner,
                icon: const Icon(Icons.open_in_new, size: 16),
                label: const Text('Open'),
              ),
              const SizedBox(width: 8),
              ElevatedButton.icon(
                onPressed: _activeSessionEndInFlight
                    ? null
                    : _endActiveSessionFromBanner,
                icon: _activeSessionEndInFlight
                    ? const SizedBox(
                        width: 14,
                        height: 14,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.flag, size: 16),
                label: Text(
                  _activeSessionEndInFlight ? 'Ending...' : 'End session',
                ),
                style: ElevatedButton.styleFrom(
                  backgroundColor: Colors.deepOrange.shade700,
                  foregroundColor: Colors.white,
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            'If headset runtime still keeps stale lock, connect to device and use "Terminate active session (rescue)".',
            style: TextStyle(
              color: Colors.orange.shade800,
              fontSize: 11,
            ),
          ),
        ],
      ),
    );
  }

  String _formatSessionState(TherapySessionRecord session) {
    final state = session.state;
    if (state != null) {
      switch (state) {
        case SessionLifecycleState.created:
          return 'CREATED';
        case SessionLifecycleState.inProgress:
          return 'IN_PROGRESS';
        case SessionLifecycleState.paused:
          return 'PAUSED';
        case SessionLifecycleState.interrupted:
          return 'INTERRUPTED';
        case SessionLifecycleState.completed:
          return 'COMPLETED';
        case SessionLifecycleState.abortedByTherapist:
          return 'ABORTED_BY_THERAPIST';
        case SessionLifecycleState.failedTechnical:
          return 'FAILED_TECHNICAL';
      }
    }

    final wire = session.stateWire.trim();
    if (wire.isNotEmpty) {
      return wire;
    }
    return 'UNKNOWN';
  }

  Future<void> _openActiveSessionFromBanner() async {
    final session = _latestActiveSession;
    if (session == null) {
      return;
    }

    final student = _latestActiveSessionStudent ??
        await StudentService.getStudent(session.studentId);
    if (student == null || !mounted) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Student profile not found for this active session.'),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    await _selectStudent(student);
  }

  Future<void> _endActiveSessionFromBanner() async {
    final session = _latestActiveSession;
    if (session == null || _activeSessionEndInFlight) {
      return;
    }

    final confirmed = await showDialog<bool>(
          context: context,
          builder: (context) => AlertDialog(
            title: const Text('End active session?'),
            content: const Text(
              'This closes the active session in session journal from setup screen. '
              'If runtime lock remains, use rescue end after device connect.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(context).pop(false),
                child: const Text('Cancel'),
              ),
              ElevatedButton(
                onPressed: () => Navigator.of(context).pop(true),
                child: const Text('End Session'),
              ),
            ],
          ),
        ) ??
        false;

    if (!confirmed || !mounted) {
      return;
    }

    final therapistId =
        (FirebaseService.currentUser?.uid.trim().isNotEmpty ?? false)
            ? FirebaseService.currentUser!.uid.trim()
            : session.therapistId;

    setState(() {
      _activeSessionEndInFlight = true;
    });

    try {
      await SessionJournalService.markSessionCompletedByTherapist(
        sessionId: session.sessionId,
        studentId: session.studentId,
        therapistId: therapistId,
        latestGameId: session.latestGameId,
        reasonCode: 'THERAPIST_CONFIRMED_END_FROM_SETUP',
        metadata: const <String, dynamic>{
          'source': 'students_setup',
          'action': 'manual_end_active_session',
        },
      );
      await SessionJournalService.appendSessionEvent(
        sessionId: session.sessionId,
        studentId: session.studentId,
        therapistId: therapistId,
        eventType: 'SESSION_ENDED_FROM_SETUP',
        gameId: session.latestGameId,
        details: const <String, dynamic>{
          'reasonCode': 'THERAPIST_CONFIRMED_END_FROM_SETUP',
        },
      );

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Active session marked as ended.'),
          ),
        );
      }
    } catch (e) {
      if (mounted) {
        _enqueueIncidentAlert(
          title: 'End active session failed',
          message: '$e',
          reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ?? 'UNSPECIFIED',
          severity: OperatorIncidentSeverity.error,
        );
      }
    } finally {
      if (mounted) {
        setState(() {
          _activeSessionEndInFlight = false;
        });
      }
      await _refreshActiveSessionBanner();
    }
  }

  void _enqueueIncidentAlert({
    required String title,
    required String message,
    String reasonCode = '',
    OperatorIncidentSeverity severity = OperatorIncidentSeverity.error,
    String source = 'students_screen',
    Map<String, dynamic> contextData = const <String, dynamic>{},
  }) {
    if (!mounted) {
      return;
    }

    final normalizedReasonCode = reasonCode.trim().toUpperCase();

    _incidentPopupQueue.enqueue(
      context,
      OperatorIncidentAlert(
        occurredAtUtc: DateTime.now().toUtc(),
        source: source,
        title: title,
        message: message,
        reasonCode:
            normalizedReasonCode.isEmpty ? 'UNKNOWN' : normalizedReasonCode,
        severity: severity,
        contextData: _buildIncidentContextData(contextData),
      ),
    );
  }

  Map<String, dynamic> _buildIncidentContextData(
    Map<String, dynamic> extras,
  ) {
    final user = FirebaseService.currentUser;
    final role =
        EntitlementService.activeAccess?.role ?? EntitlementRole.unknown;

    final context = <String, dynamic>{
      'screen': 'students_screen',
      'selectedStudentId': _selectedStudentId ?? '',
      'therapistUid': user?.uid ?? '',
      'therapistEmail': user?.email ?? '',
      'therapistRole': role.wireValue,
      'canManageStudents': role == EntitlementRole.therapist,
      'sessionRecoveryWindowMinutes':
          _therapistSessionSettings.sessionRecoveryWindowMinutes,
      'autoCloseInterruptedSessionsEnabled':
          _therapistSessionSettings.autoCloseInterruptedSessionsEnabled,
      'keepScreenAwakeWhenForeground':
          _therapistSessionSettings.keepScreenAwakeWhenForeground,
      'operatorUiLanguage':
          _therapistSessionSettings.operatorUiLanguage.wireValue,
    };
    if (extras.isNotEmpty) {
      context.addAll(extras);
    }
    return context;
  }

  bool _isLikelyOfflineError(Object error) {
    final text = error.toString().toLowerCase();
    return text.contains('network') ||
        text.contains('socket') ||
        text.contains('offline') ||
        text.contains('timeout') ||
        text.contains('failed host lookup') ||
        text.contains('unavailable');
  }

  Widget _buildStudentCard(Student student, {required bool isSelected}) {
    final note = (student.notes ?? '').trim();
    final subtitleText = note.isEmpty
        ? 'Tap to open device scan and control.'
        : '$note\nTap to open device scan and control.';

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(12),
        side: BorderSide(
          color: isSelected ? Colors.blue : Colors.grey.shade300,
          width: isSelected ? 1.5 : 1,
        ),
      ),
      child: ListTile(
        contentPadding: const EdgeInsets.fromLTRB(14, 8, 8, 8),
        leading: CircleAvatar(
          backgroundColor: isSelected ? Colors.blue.shade700 : Colors.blue,
          child: Text(
            student.initials,
            style: const TextStyle(
              color: Colors.white,
              fontWeight: FontWeight.bold,
            ),
          ),
        ),
        title: Text(
          student.fullName,
          style: const TextStyle(fontWeight: FontWeight.bold),
        ),
        subtitle: Text(
          subtitleText,
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
        ),
        trailing: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (isSelected)
              const Padding(
                padding: EdgeInsets.only(right: 2),
                child: Icon(Icons.check_circle, color: Colors.blue, size: 18),
              ),
            const Icon(Icons.chevron_right, size: 18),
            PopupMenuButton<String>(
              onSelected: EntitlementService.activeAccess?.role ==
                      EntitlementRole.therapist
                  ? (value) {
                      if (value == 'edit') {
                        _showStudentDialog(context, student: student);
                      } else if (value == 'delete') {
                        _confirmDelete(student);
                      }
                    }
                  : null,
              itemBuilder: (context) => EntitlementService.activeAccess?.role ==
                      EntitlementRole.therapist
                  ? [
                      const PopupMenuItem(
                        value: 'edit',
                        child: Row(
                          children: [
                            Icon(Icons.edit, size: 20),
                            SizedBox(width: 8),
                            Text('Edit'),
                          ],
                        ),
                      ),
                      const PopupMenuItem(
                        value: 'delete',
                        child: Row(
                          children: [
                            Icon(Icons.delete, size: 20, color: Colors.red),
                            SizedBox(width: 8),
                            Text('Delete', style: TextStyle(color: Colors.red)),
                          ],
                        ),
                      ),
                    ]
                  : [
                      const PopupMenuItem(
                        enabled: false,
                        value: 'readonly',
                        child: Text('Read-only'),
                      ),
                    ],
            ),
          ],
        ),
        onTap: () => unawaited(_selectStudent(student)),
      ),
    );
  }

  Future<void> _selectStudent(Student student) async {
    setState(() {
      _selectedStudentId = student.id;
    });

    await Navigator.push(
      context,
      MaterialPageRoute(
        builder: (context) => ScannerScreen(student: student),
      ),
    );

    if (!mounted) {
      return;
    }

    await _refreshActiveSessionBanner();
  }

  void _showStudentDialog(BuildContext context, {Student? student}) {
    final editingStudent = student;
    final isEditing = editingStudent != null;
    final firstNameController =
        TextEditingController(text: editingStudent?.firstName ?? '');
    final lastNameController =
        TextEditingController(text: editingStudent?.lastName ?? '');
    final notesController =
        TextEditingController(text: editingStudent?.notes ?? '');

    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(isEditing ? 'Edit Student' : 'Add Student'),
        content: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: firstNameController,
                decoration: const InputDecoration(
                  labelText: 'First Name',
                  border: OutlineInputBorder(),
                ),
                textCapitalization: TextCapitalization.words,
              ),
              const SizedBox(height: 16),
              TextField(
                controller: lastNameController,
                decoration: const InputDecoration(
                  labelText: 'Last Name',
                  border: OutlineInputBorder(),
                ),
                textCapitalization: TextCapitalization.words,
              ),
              const SizedBox(height: 16),
              TextField(
                controller: notesController,
                decoration: const InputDecoration(
                  labelText: 'Notes (optional)',
                  border: OutlineInputBorder(),
                ),
                maxLines: 3,
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          ElevatedButton(
            onPressed: () async {
              final firstName = firstNameController.text.trim();
              final lastName = lastNameController.text.trim();
              final notes = notesController.text.trim();

              if (firstName.isEmpty || lastName.isEmpty) {
                _enqueueIncidentAlert(
                  title: 'Student form validation failed',
                  message: 'First and last name are required.',
                  reasonCode: 'COMMAND_PRECONDITION',
                  severity: OperatorIncidentSeverity.warning,
                );
                return;
              }

              try {
                if (editingStudent != null) {
                  final result = await StudentService.updateStudent(
                    studentId: editingStudent.id,
                    firstName: firstName,
                    lastName: lastName,
                    notes: notes.isNotEmpty ? notes : null,
                  );

                  if (context.mounted) {
                    _showWriteResultSnackbar(
                      result,
                      committedMessage: 'Student updated',
                    );
                  }
                } else {
                  final result = await StudentService.addStudent(
                    firstName: firstName,
                    lastName: lastName,
                    notes: notes.isNotEmpty ? notes : null,
                  );

                  if (context.mounted) {
                    _showWriteResultSnackbar(
                      result,
                      committedMessage: 'Student added',
                    );
                  }
                }

                if (context.mounted) {
                  Navigator.pop(context);
                }
              } catch (e) {
                if (context.mounted) {
                  _enqueueIncidentAlert(
                    title: 'Student save failed',
                    message: 'Error: $e',
                    reasonCode: 'UNSPECIFIED',
                    severity: OperatorIncidentSeverity.error,
                  );
                }
              }
            },
            child: Text(isEditing ? 'Update' : 'Add'),
          ),
        ],
      ),
    );
  }

  void _confirmDelete(Student student) {
    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Delete Student'),
        content: Text('Are you sure you want to delete ${student.fullName}?'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          ElevatedButton(
            onPressed: () async {
              try {
                final result = await StudentService.deleteStudent(student.id);

                if (context.mounted) {
                  Navigator.pop(context);
                  _showWriteResultSnackbar(
                    result,
                    committedMessage: 'Student deleted',
                  );
                }
              } catch (e) {
                if (context.mounted) {
                  Navigator.pop(context);
                  _enqueueIncidentAlert(
                    title: 'Student delete failed',
                    message: 'Error: $e',
                    reasonCode: 'UNSPECIFIED',
                    severity: OperatorIncidentSeverity.error,
                  );
                }
              }
            },
            style: ElevatedButton.styleFrom(
              backgroundColor: Colors.red,
              foregroundColor: Colors.white,
            ),
            child: const Text('Delete'),
          ),
        ],
      ),
    );
  }

  Future<void> _loadTherapistSessionSettings() async {
    final settings =
        await TherapistSessionSettingsService.fetchCurrentTherapistSettings();
    if (!mounted) {
      return;
    }

    setState(() {
      _therapistSessionSettings = settings;
      _therapistSettingsLoading = false;
    });
  }

  Future<void> _openTherapistSettingsDialog() async {
    final savedSettings = await showDialog<TherapistSessionSettings>(
      context: context,
      barrierDismissible: false,
      builder: (_) => _TherapistSettingsDialog(
        initialSettings: _therapistSessionSettings,
      ),
    );

    if (savedSettings == null || !mounted) {
      return;
    }

    setState(() {
      _therapistSessionSettings = savedSettings;
    });

    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text('Session settings saved.'),
      ),
    );
  }

  Future<void> _handleLogout() async {
    final currentUser = FirebaseService.currentUser;
    final accountLabel = currentUser?.email ?? currentUser?.uid ?? 'unknown';

    final confirmed = await showDialog<bool>(
          context: context,
          builder: (context) => AlertDialog(
            title: const Text('Log out therapist?'),
            content: Text(
              'This will sign out $accountLabel and return to the login screen.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(context).pop(false),
                child: const Text('Cancel'),
              ),
              ElevatedButton(
                onPressed: () => Navigator.of(context).pop(true),
                child: const Text('Log out'),
              ),
            ],
          ),
        ) ??
        false;

    if (!confirmed) {
      return;
    }

    await StudentService.clearSessionState();
    EntitlementService.clearSessionAccess();
    TherapistSessionSettingsService.clearCachedSettings();
    await FirebaseService.signOut();
    if (mounted) {
      Navigator.of(context).pushAndRemoveUntil(
        MaterialPageRoute(builder: (_) => const LoginScreen()),
        (route) => false,
      );
    }
  }

  Future<void> _runReconciliation() async {
    final report = await StudentService.reconcilePendingWrites();
    if (!mounted) {
      return;
    }

    final message =
        'Reconcile: processed=${report.processed}, failed=${report.failed}, deferred=${report.deferred}, pending=${report.pendingAfter}';

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(message),
        backgroundColor:
            report.failed > 0 ? Colors.orange.shade700 : Colors.green.shade700,
      ),
    );
    await _refreshActiveSessionBanner();
  }

  Future<void> _refreshFromServer() async {
    await StudentService.refreshStudentsFromServer();
    await _runReconciliation();
    await _refreshActiveSessionBanner();
  }

  void _showWriteResultSnackbar(
    StudentWriteResult result, {
    required String committedMessage,
  }) {
    final isQueued = result.queuedForRetry;
    final message = isQueued
        ? 'Write queued (${result.pendingWrites} pending): ${result.statusMessage}'
        : (result.committed ? committedMessage : result.statusMessage);

    if (!result.committed) {
      _enqueueIncidentAlert(
        title: isQueued ? 'Write queued for retry' : 'Write failed',
        message: message,
        reasonCode: isQueued ? 'TCP_LINK_LOST' : 'UNSPECIFIED',
        severity: isQueued
            ? OperatorIncidentSeverity.warning
            : OperatorIncidentSeverity.error,
      );
      return;
    }

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(message),
      ),
    );
  }
}

class _TherapistSettingsDialog extends StatefulWidget {
  const _TherapistSettingsDialog({
    required this.initialSettings,
  });

  final TherapistSessionSettings initialSettings;

  @override
  State<_TherapistSettingsDialog> createState() =>
      _TherapistSettingsDialogState();
}

class _TherapistSettingsDialogState extends State<_TherapistSettingsDialog> {
  late final TextEditingController _sessionRecoveryController;
  late final TextEditingController _interruptedAutoCloseController;
  late final TextEditingController _criticalRetriesController;
  late final TextEditingController _criticalAckTimeoutController;
  late final TextEditingController _quickNotesController;
  late final TextEditingController _guidedPlanStepsController;

  late bool _autoCloseInterruptedSessionsEnabled;
  late bool _requireResumeConfirmationAfterRecoveryWindow;
  late bool _keepScreenAwakeWhenForeground;
  late bool _autoInstallOwnedGames;
  late MobileDisconnectBehavior _mobileDisconnectBehavior;
  late TherapistUiLanguage _operatorUiLanguage;
  late GuidedSessionContinuationPolicy _guidedSessionContinuationPolicy;

  bool _isSaving = false;
  String? _validationError;

  @override
  void initState() {
    super.initState();
    final initial = widget.initialSettings;
    _sessionRecoveryController = TextEditingController(
      text: initial.sessionRecoveryWindowMinutes.toString(),
    );
    _interruptedAutoCloseController = TextEditingController(
      text: initial.interruptedSessionAutoCloseHours.toString(),
    );
    _criticalRetriesController = TextEditingController(
      text: initial.criticalCommandMaxRetries.toString(),
    );
    _criticalAckTimeoutController = TextEditingController(
      text: initial.criticalCommandAckTimeoutMs.toString(),
    );
    _quickNotesController = TextEditingController(
      text: initial.timelineQuickNoteTemplates.join('\n'),
    );
    _guidedPlanStepsController = TextEditingController(
      text: GuidedSessionPlanEditorCodec.serializeStepLines(
        initial.guidedSessionPlanSteps,
      ),
    );

    _autoCloseInterruptedSessionsEnabled =
        initial.autoCloseInterruptedSessionsEnabled;
    _requireResumeConfirmationAfterRecoveryWindow =
        initial.requireResumeConfirmationAfterRecoveryWindow;
    _keepScreenAwakeWhenForeground = initial.keepScreenAwakeWhenForeground;
    _autoInstallOwnedGames = initial.autoInstallOwnedGames;
    _mobileDisconnectBehavior = initial.mobileDisconnectBehavior;
    _operatorUiLanguage = initial.operatorUiLanguage;
    _guidedSessionContinuationPolicy = initial.guidedSessionContinuationPolicy;
  }

  @override
  void dispose() {
    _sessionRecoveryController.dispose();
    _interruptedAutoCloseController.dispose();
    _criticalRetriesController.dispose();
    _criticalAckTimeoutController.dispose();
    _quickNotesController.dispose();
    _guidedPlanStepsController.dispose();
    super.dispose();
  }

  int? _parseBoundedInt({
    required String rawValue,
    required String label,
    required int min,
    required int max,
    required void Function(String message) onError,
  }) {
    final parsed = int.tryParse(rawValue.trim());
    if (parsed == null) {
      onError('$label must be a number.');
      return null;
    }

    if (parsed < min || parsed > max) {
      onError('$label must be between $min and $max.');
      return null;
    }

    return parsed;
  }

  Future<void> _closeSafely([TherapistSessionSettings? result]) async {
    FocusScope.of(context).unfocus();
    await Future<void>.delayed(const Duration(milliseconds: 16));
    if (!mounted) {
      return;
    }
    Navigator.of(context).pop(result);
  }

  void _resetDefaults() {
    if (_isSaving) {
      return;
    }
    final defaults = TherapistSessionSettings.defaults();
    _sessionRecoveryController.text =
        defaults.sessionRecoveryWindowMinutes.toString();
    _interruptedAutoCloseController.text =
        defaults.interruptedSessionAutoCloseHours.toString();
    _criticalRetriesController.text =
        defaults.criticalCommandMaxRetries.toString();
    _criticalAckTimeoutController.text =
        defaults.criticalCommandAckTimeoutMs.toString();
    _quickNotesController.text = defaults.timelineQuickNoteTemplates.join('\n');
    _guidedPlanStepsController.text =
        GuidedSessionPlanEditorCodec.serializeStepLines(
            defaults.guidedSessionPlanSteps);
    setState(() {
      _autoCloseInterruptedSessionsEnabled =
          defaults.autoCloseInterruptedSessionsEnabled;
      _requireResumeConfirmationAfterRecoveryWindow =
          defaults.requireResumeConfirmationAfterRecoveryWindow;
      _keepScreenAwakeWhenForeground = defaults.keepScreenAwakeWhenForeground;
      _autoInstallOwnedGames = defaults.autoInstallOwnedGames;
      _mobileDisconnectBehavior = defaults.mobileDisconnectBehavior;
      _operatorUiLanguage = defaults.operatorUiLanguage;
      _guidedSessionContinuationPolicy =
          defaults.guidedSessionContinuationPolicy;
      _validationError = null;
    });
  }

  Future<void> _handleSave() async {
    if (_isSaving) {
      return;
    }

    String? validationMessage;
    void captureError(String message) {
      validationMessage ??= message;
    }

    final sessionRecoveryWindowMinutes = _parseBoundedInt(
      rawValue: _sessionRecoveryController.text,
      label: 'Session recovery window',
      min: TherapistSessionSettings.minSessionRecoveryWindowMinutes,
      max: TherapistSessionSettings.maxSessionRecoveryWindowMinutes,
      onError: captureError,
    );
    final interruptedSessionAutoCloseHours = _parseBoundedInt(
      rawValue: _interruptedAutoCloseController.text,
      label: 'Interrupted session auto-close',
      min: TherapistSessionSettings.minInterruptedSessionAutoCloseHours,
      max: TherapistSessionSettings.maxInterruptedSessionAutoCloseHours,
      onError: captureError,
    );
    final criticalCommandMaxRetries = _parseBoundedInt(
      rawValue: _criticalRetriesController.text,
      label: 'Critical command retry count',
      min: TherapistSessionSettings.minCriticalCommandMaxRetries,
      max: TherapistSessionSettings.maxCriticalCommandMaxRetries,
      onError: captureError,
    );
    final criticalCommandAckTimeoutMs = _parseBoundedInt(
      rawValue: _criticalAckTimeoutController.text,
      label: 'Critical command ACK timeout',
      min: TherapistSessionSettings.minCriticalCommandAckTimeoutMs,
      max: TherapistSessionSettings.maxCriticalCommandAckTimeoutMs,
      onError: captureError,
    );

    if (validationMessage != null ||
        sessionRecoveryWindowMinutes == null ||
        interruptedSessionAutoCloseHours == null ||
        criticalCommandMaxRetries == null ||
        criticalCommandAckTimeoutMs == null) {
      setState(() {
        _validationError = validationMessage ?? 'Invalid settings.';
      });
      return;
    }

    final templateSet = <String>{};
    final timelineQuickNoteTemplates = _quickNotesController.text
        .split('\n')
        .map((entry) => entry.trim())
        .where((entry) => entry.isNotEmpty)
        .where((entry) => templateSet.add(entry))
        .toList(growable: false);

    List<GuidedSessionPlanStep> guidedSessionPlanSteps;
    try {
      guidedSessionPlanSteps = GuidedSessionPlanEditorCodec.parseStepLines(
        _guidedPlanStepsController.text,
      );
    } on FormatException catch (e) {
      setState(() {
        _validationError = e.message;
      });
      return;
    }

    if (guidedSessionPlanSteps.isEmpty) {
      setState(() {
        _validationError =
            'Guided session plan requires at least one game step.';
      });
      return;
    }

    final settings = TherapistSessionSettings.fromMap(
      <String, dynamic>{
        'sessionRecoveryWindowMinutes': sessionRecoveryWindowMinutes,
        'interruptedSessionAutoCloseHours': interruptedSessionAutoCloseHours,
        'autoCloseInterruptedSessionsEnabled':
            _autoCloseInterruptedSessionsEnabled,
        'requireResumeConfirmationAfterRecoveryWindow':
            _requireResumeConfirmationAfterRecoveryWindow,
        'criticalCommandMaxRetries': criticalCommandMaxRetries,
        'criticalCommandAckTimeoutMs': criticalCommandAckTimeoutMs,
        'adaptiveDifficultyEnabled':
            widget.initialSettings.adaptiveDifficultyEnabled,
        'adaptiveDifficultySensitivity':
            widget.initialSettings.adaptiveDifficultySensitivity,
        'labelPipelineEnabled': widget.initialSettings.labelPipelineEnabled,
        'keepScreenAwakeWhenForeground': _keepScreenAwakeWhenForeground,
        'autoInstallOwnedGames': _autoInstallOwnedGames,
        'mobileDisconnectBehavior': _mobileDisconnectBehavior.wireValue,
        'operatorUiLanguage': _operatorUiLanguage.wireValue,
        'timelineQuickNoteTemplates': timelineQuickNoteTemplates,
        'defaultGuidedSessionPlanId': 'default_parent_guided_plan',
        'guidedSessionContinuationPolicy':
            _guidedSessionContinuationPolicy.wireValue,
        'guidedSessionPlanSteps':
            guidedSessionPlanSteps.map((step) => step.toMap()).toList(),
      },
    );

    setState(() {
      _isSaving = true;
      _validationError = null;
    });

    final saved =
        await TherapistSessionSettingsService.saveCurrentTherapistSettings(
      settings,
    );
    if (!mounted) {
      return;
    }

    if (!saved) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Could not save settings right now.'),
          backgroundColor: Colors.orange,
        ),
      );
      await _closeSafely();
      return;
    }

    await _closeSafely(settings);
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Session settings'),
      content: SizedBox(
        width: 520,
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              TextField(
                controller: _sessionRecoveryController,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'Session Recovery Window (min)',
                  border: OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _interruptedAutoCloseController,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'Interrupted Session Auto-Close (h)',
                  border: OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 12),
              SwitchListTile(
                value: _autoCloseInterruptedSessionsEnabled,
                contentPadding: EdgeInsets.zero,
                title: const Text('Enable Auto-Close Interrupted Sessions'),
                onChanged: _isSaving
                    ? null
                    : (value) {
                        setState(() {
                          _autoCloseInterruptedSessionsEnabled = value;
                        });
                      },
              ),
              SwitchListTile(
                value: _requireResumeConfirmationAfterRecoveryWindow,
                contentPadding: EdgeInsets.zero,
                title: const Text(
                  'Require Confirmation After Recovery Window',
                ),
                onChanged: _isSaving
                    ? null
                    : (value) {
                        setState(() {
                          _requireResumeConfirmationAfterRecoveryWindow = value;
                        });
                      },
              ),
              SwitchListTile(
                value: _keepScreenAwakeWhenForeground,
                contentPadding: EdgeInsets.zero,
                title: const Text('Prevent phone sleep while app is active'),
                subtitle: const Text(
                  'Keeps the screen awake only when the app is in foreground.',
                ),
                onChanged: _isSaving
                    ? null
                    : (value) {
                        setState(() {
                          _keepScreenAwakeWhenForeground = value;
                        });
                      },
              ),
              SwitchListTile(
                value: _autoInstallOwnedGames,
                contentPadding: EdgeInsets.zero,
                title: const Text('Auto-install game after Buy (sim)'),
                subtitle: const Text(
                  'When enabled, purchased game installs immediately after moving to Owned tab.',
                ),
                onChanged: _isSaving
                    ? null
                    : (value) {
                        setState(() {
                          _autoInstallOwnedGames = value;
                        });
                      },
              ),
              const SizedBox(height: 4),
              DropdownButtonFormField<MobileDisconnectBehavior>(
                initialValue: _mobileDisconnectBehavior,
                decoration: const InputDecoration(
                  labelText: 'Mobile disconnect behavior',
                  border: OutlineInputBorder(),
                ),
                items: const [
                  DropdownMenuItem<MobileDisconnectBehavior>(
                    value: MobileDisconnectBehavior.pause,
                    child: Text('Pause game (default)'),
                  ),
                  DropdownMenuItem<MobileDisconnectBehavior>(
                    value: MobileDisconnectBehavior.continueGameplay,
                    child: Text('Continue game'),
                  ),
                ],
                onChanged: _isSaving
                    ? null
                    : (value) {
                        if (value == null) {
                          return;
                        }
                        setState(() {
                          _mobileDisconnectBehavior = value;
                        });
                      },
              ),
              const SizedBox(height: 4),
              DropdownButtonFormField<TherapistUiLanguage>(
                initialValue: _operatorUiLanguage,
                decoration: const InputDecoration(
                  labelText: 'Incident message language',
                  border: OutlineInputBorder(),
                ),
                items: const [
                  DropdownMenuItem<TherapistUiLanguage>(
                    value: TherapistUiLanguage.english,
                    child: Text('English (en)'),
                  ),
                  DropdownMenuItem<TherapistUiLanguage>(
                    value: TherapistUiLanguage.polish,
                    child: Text('Polski (pl)'),
                  ),
                ],
                onChanged: _isSaving
                    ? null
                    : (value) {
                        if (value == null) {
                          return;
                        }
                        setState(() {
                          _operatorUiLanguage = value;
                        });
                      },
              ),
              const SizedBox(height: 4),
              TextField(
                controller: _criticalRetriesController,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'Critical Command Retry Count',
                  border: OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _criticalAckTimeoutController,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'Critical Command ACK Timeout (ms)',
                  border: OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _quickNotesController,
                minLines: 3,
                maxLines: 6,
                decoration: const InputDecoration(
                  labelText: 'Timeline Quick Notes Templates',
                  hintText: 'One template per line',
                  border: OutlineInputBorder(),
                ),
              ),
              const SizedBox(height: 12),
              DropdownButtonFormField<GuidedSessionContinuationPolicy>(
                initialValue: _guidedSessionContinuationPolicy,
                decoration: const InputDecoration(
                  labelText: 'Guided continuation policy',
                  border: OutlineInputBorder(),
                ),
                items: const [
                  DropdownMenuItem<GuidedSessionContinuationPolicy>(
                    value: GuidedSessionContinuationPolicy.manual,
                    child: Text('Manual decision'),
                  ),
                  DropdownMenuItem<GuidedSessionContinuationPolicy>(
                    value: GuidedSessionContinuationPolicy
                        .resumeUnderRecoveryWindow,
                    child: Text('Auto resume under window'),
                  ),
                  DropdownMenuItem<GuidedSessionContinuationPolicy>(
                    value: GuidedSessionContinuationPolicy.resumeAlways,
                    child: Text('Always auto resume'),
                  ),
                ],
                onChanged: _isSaving
                    ? null
                    : (value) {
                        if (value == null) {
                          return;
                        }
                        setState(() {
                          _guidedSessionContinuationPolicy = value;
                        });
                      },
              ),
              const SizedBox(height: 12),
              TextField(
                controller: _guidedPlanStepsController,
                minLines: 3,
                maxLines: 8,
                decoration: const InputDecoration(
                  labelText: 'Guided plan steps',
                  hintText: 'One step per line: gameId|{"presetKey":"value"}',
                  helperText:
                      'Example: demo_cube_clicker|{"cubeCount":10,"cubeSpeed":0.7}',
                  border: OutlineInputBorder(),
                ),
              ),
              if (_validationError != null) ...[
                const SizedBox(height: 10),
                Text(
                  _validationError!,
                  style: TextStyle(
                    color: Colors.red.shade700,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: _isSaving ? null : () => _closeSafely(),
          child: const Text('Cancel'),
        ),
        TextButton(
          onPressed: _isSaving ? null : _resetDefaults,
          child: const Text('Reset defaults'),
        ),
        ElevatedButton(
          onPressed: _isSaving ? null : _handleSave,
          child: _isSaving
              ? const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Text('Save'),
        ),
      ],
    );
  }
}
