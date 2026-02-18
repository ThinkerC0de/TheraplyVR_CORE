import 'package:flutter/material.dart';
import 'package:flutter_controller/models/entitlement_access.dart';
import 'package:flutter_controller/models/student.dart';
import 'package:flutter_controller/models/student_roster_sync.dart';
import 'package:flutter_controller/services/student_service.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:flutter_controller/services/entitlement_service.dart';
import 'package:flutter_controller/screens/login_screen.dart';
import 'package:flutter_controller/screens/scanner_screen.dart';

class StudentsScreen extends StatefulWidget {
  const StudentsScreen({super.key});

  @override
  State<StudentsScreen> createState() => _StudentsScreenState();
}

class _StudentsScreenState extends State<StudentsScreen> {
  String? _selectedStudentId;

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
        title: const Text('Students'),
        actions: [
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
                              ? 'Read-only mode. Student management is disabled.'
                              : 'Manage and open student sessions.',
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
                            'No students yet',
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
                                'Your students',
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
        onTap: () => _selectStudent(student),
      ),
    );
  }

  void _selectStudent(Student student) {
    setState(() {
      _selectedStudentId = student.id;
    });

    Navigator.push(
      context,
      MaterialPageRoute(
        builder: (context) => ScannerScreen(student: student),
      ),
    );
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
                ScaffoldMessenger.of(context).showSnackBar(
                  const SnackBar(
                    content: Text('First and last name are required'),
                    backgroundColor: Colors.red,
                  ),
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
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(
                      content: Text('Error: $e'),
                      backgroundColor: Colors.red,
                    ),
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
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(
                      content: Text('Error: $e'),
                      backgroundColor: Colors.red,
                    ),
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
  }

  Future<void> _refreshFromServer() async {
    await StudentService.refreshStudentsFromServer();
    await _runReconciliation();
  }

  void _showWriteResultSnackbar(
    StudentWriteResult result, {
    required String committedMessage,
  }) {
    final isQueued = result.queuedForRetry;
    final message = isQueued
        ? 'Write queued (${result.pendingWrites} pending): ${result.statusMessage}'
        : (result.committed ? committedMessage : result.statusMessage);

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(message),
        backgroundColor: isQueued
            ? Colors.orange.shade700
            : (result.committed ? null : Colors.red.shade700),
      ),
    );
  }
}
