import 'package:cloud_firestore/cloud_firestore.dart';

class Student {
  final String id;
  final String firstName;
  final String lastName;
  final String? notes;
  final DateTime createdAt;
  final String therapistId;
  
  Student({
    required this.id,
    required this.firstName,
    required this.lastName,
    this.notes,
    required this.createdAt,
    required this.therapistId,
  });
  
  /// Full name getter
  String get fullName => '$firstName $lastName';
  
  /// Initials getter (for avatar placeholder)
  String get initials {
    final first = firstName.isNotEmpty ? firstName[0].toUpperCase() : '';
    final last = lastName.isNotEmpty ? lastName[0].toUpperCase() : '';
    return '$first$last';
  }
  
  /// Create from Firestore document
  factory Student.fromFirestore(DocumentSnapshot doc) {
    final data = doc.data() as Map<String, dynamic>;
    
    return Student(
      id: doc.id,
      firstName: data['firstName'] ?? '',
      lastName: data['lastName'] ?? '',
      notes: data['notes'],
      createdAt: (data['createdAt'] as Timestamp).toDate(),
      therapistId: data['therapistId'] ?? '',
    );
  }
  
  /// Convert to Firestore document
  Map<String, dynamic> toFirestore() {
    return {
      'firstName': firstName,
      'lastName': lastName,
      'notes': notes,
      'createdAt': Timestamp.fromDate(createdAt),
      'therapistId': therapistId,
    };
  }
  
  /// Create copy with updated fields
  Student copyWith({
    String? id,
    String? firstName,
    String? lastName,
    String? notes,
    DateTime? createdAt,
    String? therapistId,
  }) {
    return Student(
      id: id ?? this.id,
      firstName: firstName ?? this.firstName,
      lastName: lastName ?? this.lastName,
      notes: notes ?? this.notes,
      createdAt: createdAt ?? this.createdAt,
      therapistId: therapistId ?? this.therapistId,
    );
  }
}
