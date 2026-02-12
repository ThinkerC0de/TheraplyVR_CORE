import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:cloud_firestore/cloud_firestore.dart';

class FirebaseService {
  static Future<void> initialize() async {
    try {
      await Firebase.initializeApp();
      print('[Firebase] ✅ Initialized successfully');
    } catch (e) {
      print('[Firebase] ❌ Initialization failed: $e');
      rethrow;
    }
  }
  
  static FirebaseAuth get auth => FirebaseAuth.instance;
  static FirebaseFirestore get firestore => FirebaseFirestore.instance;
  
  static Future<User?> signIn(String email, String password) async {
    try {
      final credential = await auth.signInWithEmailAndPassword(
        email: email,
        password: password,
      );
      
      print('[Firebase] ✅ Signed in: ${credential.user?.email}');
      return credential.user;
    } catch (e) {
      print('[Firebase] ❌ Sign in failed: $e');
      rethrow;
    }
  }
  
  static Future<void> signOut() async {
    await auth.signOut();
    print('[Firebase] ✅ Signed out');
  }
  
  static Stream<User?> authStateChanges() {
    return auth.authStateChanges();
  }
  
  static User? get currentUser => auth.currentUser;
}
