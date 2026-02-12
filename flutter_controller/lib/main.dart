import 'package:flutter/material.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:flutter_controller/screens/login_screen.dart';

void main() async {
  WidgetsFlutterBinding.ensureInitialized();
  
  // Initialize Firebase
  await FirebaseService.initialize();
  
  runApp(const TheraplyControllerApp());
}

class TheraplyControllerApp extends StatelessWidget {
  const TheraplyControllerApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Theraply Controller',
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: Colors.blue),
        useMaterial3: true,
      ),
      home: const LoginScreen(),
      debugShowCheckedModeBanner: false,
    );
  }
}
