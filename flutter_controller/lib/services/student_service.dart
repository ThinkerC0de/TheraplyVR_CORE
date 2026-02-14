import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:flutter_controller/models/student.dart';
import 'package:flutter_controller/services/firebase_service.dart';

class StudentService {
  static final CollectionReference _studentsCollection =
      FirebaseService.firestore.collection('students');
  
  /// Get current therapist ID
  static String? get _currentTherapistId =>
      FirebaseService.currentUser?.uid;
  
  /// Get all students for current therapist
  static Stream<List<Student>> getStudents() {
    final therapistId = _currentTherapistId;
    
    if (therapistId == null) {
      return Stream.value([]);
    }
    
    return _studentsCollection
        .where('therapistId', isEqualTo: therapistId)
        .orderBy('createdAt', descending: true)
        .snapshots()
        .map((snapshot) {
      return snapshot.docs
          .map((doc) => Student.fromFirestore(doc))
          .toList();
    });
  }
  
  /// Add new student
  static Future<String> addStudent({
    required String firstName,
    required String lastName,
    String? notes,
  }) async {
    final therapistId = _currentTherapistId;
    
    if (therapistId == null) {
      throw Exception('No therapist logged in');
    }
    
    final student = Student(
      id: '', // Will be set by Firestore
      firstName: firstName,
      lastName: lastName,
      notes: notes,
      createdAt: DateTime.now(),
      therapistId: therapistId,
    );
    
    final docRef = await _studentsCollection.add(student.toFirestore());
    
    print('[StudentService] ✅ Added student: ${student.fullName} (${docRef.id})');
    
    return docRef.id;
  }
  
  /// Update existing student
  static Future<void> updateStudent({
    required String studentId,
    required String firstName,
    required String lastName,
    String? notes,
  }) async {
    await _studentsCollection.doc(studentId).update({
      'firstName': firstName,
      'lastName': lastName,
      'notes': notes,
    });
    
    print('[StudentService] ✅ Updated student: $studentId');
  }
  
  /// Delete student
  static Future<void> deleteStudent(String studentId) async {
    await _studentsCollection.doc(studentId).delete();
    
    print('[StudentService] ✅ Deleted student: $studentId');
  }
  
  /// Get single student by ID
  static Future<Student?> getStudent(String studentId) async {
    final doc = await _studentsCollection.doc(studentId).get();
    
    if (!doc.exists) {
      return null;
    }
    
    return Student.fromFirestore(doc);
  }
}
