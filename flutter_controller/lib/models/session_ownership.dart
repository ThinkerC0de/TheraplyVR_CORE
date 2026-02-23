class SessionOwnership {
  static String normalizeId(String? value, {required String fallback}) {
    final normalized = (value ?? '').trim();
    if (normalized.isNotEmpty) {
      return normalized;
    }
    return fallback.trim();
  }

  static String resolveStudentId({
    required String? studentId,
    required String? patientId,
    String fallback = '',
  }) {
    final normalizedStudent = (studentId ?? '').trim();
    if (normalizedStudent.isNotEmpty) {
      return normalizedStudent;
    }

    final normalizedPatient = (patientId ?? '').trim();
    if (normalizedPatient.isNotEmpty) {
      return normalizedPatient;
    }

    return fallback.trim();
  }

  static String ownerKey({
    required String therapistId,
    required String studentId,
  }) {
    final normalizedTherapist = therapistId.trim();
    final normalizedStudent = studentId.trim();
    if (normalizedTherapist.isEmpty || normalizedStudent.isEmpty) {
      return '';
    }
    return '$normalizedTherapist|$normalizedStudent';
  }

  static String sessionKey({
    required String therapistId,
    required String studentId,
    required String sessionId,
  }) {
    final normalizedSession = sessionId.trim();
    if (normalizedSession.isEmpty) {
      return '';
    }

    final ownershipKey = ownerKey(
      therapistId: therapistId,
      studentId: studentId,
    );
    if (ownershipKey.isEmpty) {
      return '';
    }

    return '$ownershipKey|$normalizedSession';
  }

  static bool matchesOwner({
    required String expectedTherapistId,
    required String expectedStudentId,
    required String incomingTherapistId,
    required String incomingStudentId,
  }) {
    final expectedKey = ownerKey(
      therapistId: expectedTherapistId,
      studentId: expectedStudentId,
    );
    if (expectedKey.isEmpty) {
      return false;
    }

    final incomingKey = ownerKey(
      therapistId: incomingTherapistId,
      studentId: incomingStudentId,
    );
    if (incomingKey.isEmpty) {
      return false;
    }

    return expectedKey == incomingKey;
  }
}
