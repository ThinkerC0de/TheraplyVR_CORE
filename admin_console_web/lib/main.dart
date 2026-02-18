import 'package:flutter/material.dart';
import 'package:admin_console_web/screens/login_screen.dart';
import 'package:admin_console_web/screens/ops_dashboard_screen.dart';
import 'package:admin_console_web/services/firebase_service.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await FirebaseService.initialize();
  runApp(const AdminConsoleApp());
}

class AdminConsoleApp extends StatelessWidget {
  const AdminConsoleApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Theraply Admin Console',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: Colors.blueGrey),
        useMaterial3: true,
      ),
      home: StreamBuilder(
        stream: FirebaseService.authStateChanges(),
        builder: (context, snapshot) {
          final user = snapshot.data;
          if (user == null) {
            return const LoginScreen();
          }
          return const OpsDashboardScreen();
        },
      ),
    );
  }
}
