import 'package:firebase_auth/firebase_auth.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:cloud_firestore/cloud_firestore.dart';
import 'package:flutter/foundation.dart';

class FirebaseService {
  static const String _apiKey = String.fromEnvironment('FIREBASE_API_KEY');
  static const String _appId = String.fromEnvironment('FIREBASE_APP_ID');
  static const String _messagingSenderId =
      String.fromEnvironment('FIREBASE_MESSAGING_SENDER_ID');
  static const String _projectId = String.fromEnvironment('FIREBASE_PROJECT_ID');
  static const String _authDomain =
      String.fromEnvironment('FIREBASE_AUTH_DOMAIN', defaultValue: '');
  static const String _storageBucket =
      String.fromEnvironment('FIREBASE_STORAGE_BUCKET', defaultValue: '');
  static const String _measurementId =
      String.fromEnvironment('FIREBASE_MEASUREMENT_ID', defaultValue: '');

  static bool get hasWebOptions {
    return _apiKey.isNotEmpty &&
        _appId.isNotEmpty &&
        _messagingSenderId.isNotEmpty &&
        _projectId.isNotEmpty;
  }

  static Future<void> initialize() async {
    try {
      if (kIsWeb && hasWebOptions) {
        await Firebase.initializeApp(
          options: FirebaseOptions(
            apiKey: _apiKey,
            appId: _appId,
            messagingSenderId: _messagingSenderId,
            projectId: _projectId,
            authDomain: _authDomain.isEmpty ? null : _authDomain,
            storageBucket: _storageBucket.isEmpty ? null : _storageBucket,
            measurementId: _measurementId.isEmpty ? null : _measurementId,
          ),
        );
      } else {
        await Firebase.initializeApp();
      }
    } catch (e) {
      throw Exception(
        'Firebase init failed. For web pass dart-defines FIREBASE_API_KEY/FIREBASE_APP_ID/'
        'FIREBASE_MESSAGING_SENDER_ID/FIREBASE_PROJECT_ID. Details: $e',
      );
    }
  }

  static FirebaseAuth get auth => FirebaseAuth.instance;
  static FirebaseFirestore get firestore => FirebaseFirestore.instance;

  static Future<User?> signIn(String email, String password) async {
    final credential = await auth.signInWithEmailAndPassword(
      email: email,
      password: password,
    );
    return credential.user;
  }

  static Future<void> signOut() async {
    await auth.signOut();
  }

  static Stream<User?> authStateChanges() => auth.authStateChanges();
  static User? get currentUser => auth.currentUser;
}
