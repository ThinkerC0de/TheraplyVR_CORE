import 'package:cloud_firestore/cloud_firestore.dart';

class Student {
  final String id;
  final String firstName;
  final String lastName;
  final String? notes;
  final DateTime createdAt;
  final DateTime updatedAt;
  final int revision;
  final String therapistId;
  
  Student({
    required this.id,
    required this.firstName,
    required this.lastName,
    this.notes,
    required this.createdAt,
    required this.updatedAt,
    required this.revision,
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
    final createdAt = _readDateTime(data['createdAt']) ?? DateTime.now().toUtc();
    
    return Student(
      id: doc.id,
      firstName: data['firstName'] ?? '',
      lastName: data['lastName'] ?? '',
      notes: data['notes'],
      createdAt: createdAt,
      updatedAt: _readDateTime(data['updatedAt']) ?? createdAt,
      revision: data['revision'] as int? ?? 0,
      therapistId: data['therapistId'] ?? '',
    );
  }

  factory Student.fromJson(Map<String, dynamic> json) {
    final createdAt = _readDateTime(json['createdAt']) ?? DateTime.now().toUtc();
    return Student(
      id: json['id'] as String? ?? '',
      firstName: json['firstName'] as String? ?? '',
      lastName: json['lastName'] as String? ?? '',
      notes: json['notes'] as String?,
      createdAt: createdAt,
      updatedAt: _readDateTime(json['updatedAt']) ?? createdAt,
      revision: json['revision'] as int? ?? 0,
      therapistId: json['therapistId'] as String? ?? '',
    );
  }
  
  /// Convert to Firestore document
  Map<String, dynamic> toFirestore() {
    return {
      'firstName': firstName,
      'lastName': lastName,
      'notes': notes,
      'createdAt': Timestamp.fromDate(createdAt.toUtc()),
      'updatedAt': Timestamp.fromDate(updatedAt.toUtc()),
      'revision': revision,
      'therapistId': therapistId,
    };
  }

  Map<String, dynamic> toJson() {
    return <String, dynamic>{
      'id': id,
      'firstName': firstName,
      'lastName': lastName,
      'notes': notes,
      'createdAt': createdAt.toUtc().toIso8601String(),
      'updatedAt': updatedAt.toUtc().toIso8601String(),
      'revision': revision,
      'therapistId': therapistId,
    };
  }
  
  /// Create copy with updated fields
  Student copyWith({
    String? id,
    String? firstName,
    String? lastName,
    Object? notes = _notesSentinel,
    DateTime? createdAt,
    DateTime? updatedAt,
    int? revision,
    String? therapistId,
  }) {
    return Student(
      id: id ?? this.id,
      firstName: firstName ?? this.firstName,
      lastName: lastName ?? this.lastName,
      notes: identical(notes, _notesSentinel) ? this.notes : notes as String?,
      createdAt: createdAt ?? this.createdAt,
      updatedAt: updatedAt ?? this.updatedAt,
      revision: revision ?? this.revision,
      therapistId: therapistId ?? this.therapistId,
    );
  }

  static DateTime? _readDateTime(dynamic value) {
    if (value is Timestamp) {
      return value.toDate().toUtc();
    }

    if (value is DateTime) {
      return value.toUtc();
    }

    if (value is String) {
      return DateTime.tryParse(value)?.toUtc();
    }

    return null;
  }
}

const Object _notesSentinel = Object();
