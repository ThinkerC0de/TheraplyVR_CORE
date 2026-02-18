import 'package:flutter/material.dart';
import 'package:flutter_controller/models/entitlement_access.dart';
import 'package:flutter_controller/models/student.dart';
import 'package:flutter_controller/models/student_roster_sync.dart';
import 'package:flutter_controller/services/student_service.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:flutter_controller/services/entitlement_service.dart';
import 'package:flutter_controller/screens/entitlement_ops_screen.dart';
import 'package:flutter_controller/screens/scanner_screen.dart';

class StudentsScreen extends StatefulWidget {
  const StudentsScreen({super.key});

  @override
  State<StudentsScreen> createState() => _StudentsScreenState();
}

class _StudentsScreenState extends State<StudentsScreen> {
  static const bool _enableEntitlementOpsPanel = bool.fromEnvironment(
    'ENABLE_ENTITLEMENT_OPS_PANEL',
    defaultValue: false,
  );

  @override
  Widget build(BuildContext context) {
    final canManageStudents =
        EntitlementService.activeAccess?.role == EntitlementRole.therapist;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Students'),
        actions: [
          IconButton(
            icon: const Icon(Icons.sync),
            onPressed: _runReconciliation,
            tooltip: 'Reconcile pending writes',
          ),
          if (_enableEntitlementOpsPanel)
            IconButton(
              icon: const Icon(Icons.admin_panel_settings),
              onPressed: _openEntitlementOpsPanel,
              tooltip: 'Entitlement Ops',
            ),
          IconButton(
            icon: const Icon(Icons.logout),
            onPressed: _handleLogout,
            tooltip: 'Logout',
          ),
        ],
      ),
      body: Column(
        children: [
          StreamBuilder<int>(
            stream: StudentService.watchPendingWritesCount(),
            builder: (context, snapshot) {
              final pendingWrites = snapshot.data ?? 0;
              if (pendingWrites <= 0) {
                return const SizedBox.shrink();
              }

              return Container(
                width: double.infinity,
                color: Colors.amber.shade50,
                padding:
                    const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                child: Text(
                  '$pendingWrites pending write(s). Tap sync to reconcile.',
                  style: TextStyle(
                    color: Colors.amber.shade900,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              );
            },
          ),
          Expanded(
            child: StreamBuilder<List<Student>>(
              stream: StudentService.getStudents(),
              builder: (context, snapshot) {
                if (snapshot.hasError) {
                  return Center(
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        const Icon(Icons.error_outline,
                            size: 64, color: Colors.red),
                        const SizedBox(height: 16),
                        Text('Error: ${snapshot.error}'),
                      ],
                    ),
                  );
                }

                if (!snapshot.hasData) {
                  return const Center(
                    child: CircularProgressIndicator(),
                  );
                }

                final students = snapshot.data!;

                if (students.isEmpty) {
                  return Center(
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(Icons.school_outlined,
                            size: 64, color: Colors.grey[400]),
                        const SizedBox(height: 16),
                        const Text(
                          'No students yet',
                          style: TextStyle(
                              fontSize: 18, fontWeight: FontWeight.w500),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'Tap + to add your first student',
                          style: TextStyle(color: Colors.grey[600]),
                        ),
                      ],
                    ),
                  );
                }

                return RefreshIndicator(
                  onRefresh: _refreshFromServer,
                  child: ListView.builder(
                    itemCount: students.length,
                    itemBuilder: (context, index) {
                      final student = students[index];
                      return _buildStudentCard(student);
                    },
                  ),
                );
              },
            ),
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton(
        onPressed: canManageStudents ? () => _showStudentDialog(context) : null,
        tooltip: 'Add Student',
        child: const Icon(Icons.add),
      ),
    );
  }
  
  Widget _buildStudentCard(Student student) {
    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: ListTile(
        leading: CircleAvatar(
          backgroundColor: Colors.blue,
          child: Text(
            student.initials,
            style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold),
          ),
        ),
        title: Text(
          student.fullName,
          style: const TextStyle(fontWeight: FontWeight.bold),
        ),
        subtitle: student.notes != null && student.notes!.isNotEmpty
            ? Text(
                student.notes!,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              )
            : null,
        trailing: PopupMenuButton<String>(
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
        onTap: () => _selectStudent(student),
      ),
    );
  }
  
  void _selectStudent(Student student) {
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
    final firstNameController = TextEditingController(text: editingStudent?.firstName ?? '');
    final lastNameController = TextEditingController(text: editingStudent?.lastName ?? '');
    final notesController = TextEditingController(text: editingStudent?.notes ?? '');
    
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
    await StudentService.clearSessionState();
    EntitlementService.clearSessionAccess();
    await FirebaseService.signOut();
    if (mounted) {
      Navigator.pop(context);
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

  void _openEntitlementOpsPanel() {
    Navigator.push(
      context,
      MaterialPageRoute(
        builder: (context) => const EntitlementOpsScreen(),
      ),
    );
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
