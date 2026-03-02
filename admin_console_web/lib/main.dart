import 'package:flutter/material.dart';
import 'package:firebase_auth/firebase_auth.dart';
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
    const canvasColor = Color(0xFFEFF3F8);
    const panelBorderColor = Color(0xFFD2DAE5);
    const accentTeal = Color(0xFF0E766E);
    const accentGold = Color(0xFFB7791F);
    return MaterialApp(
      title: 'Theraply Admin Console',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: const ColorScheme.light(
          primary: accentTeal,
          secondary: accentGold,
          surface: Colors.white,
          onSurface: Color(0xFF1F2937),
          error: Color(0xFFB42318),
        ),
        useMaterial3: true,
        fontFamily: 'Trebuchet MS',
        scaffoldBackgroundColor: canvasColor,
        appBarTheme: const AppBarTheme(
          backgroundColor: Color(0xFFE5EBF3),
          foregroundColor: Color(0xFF0F172A),
          elevation: 0,
          scrolledUnderElevation: 0,
          centerTitle: false,
          titleTextStyle: TextStyle(
            fontSize: 22,
            fontWeight: FontWeight.w700,
            color: Color(0xFF0F172A),
          ),
        ),
        cardTheme: CardThemeData(
          color: Colors.white,
          elevation: 0,
          margin: EdgeInsets.zero,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(14),
            side: const BorderSide(color: panelBorderColor),
          ),
        ),
        inputDecorationTheme: InputDecorationTheme(
          filled: true,
          fillColor: Colors.white,
          border: OutlineInputBorder(
            borderRadius: BorderRadius.circular(10),
          ),
          enabledBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(10),
            borderSide: const BorderSide(color: panelBorderColor),
          ),
          focusedBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(10),
            borderSide: const BorderSide(
              color: accentTeal,
              width: 1.4,
            ),
          ),
        ),
        filledButtonTheme: FilledButtonThemeData(
          style: FilledButton.styleFrom(
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(10),
            ),
          ),
        ),
        outlinedButtonTheme: OutlinedButtonThemeData(
          style: OutlinedButton.styleFrom(
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(10),
            ),
          ),
        ),
        chipTheme: ThemeData.light().chipTheme.copyWith(
              shape: const StadiumBorder(),
              side: const BorderSide(color: panelBorderColor),
            ),
      ),
      home: StreamBuilder<User?>(
        stream: FirebaseService.authStateChanges(),
        builder: (context, snapshot) {
          final user = snapshot.data;
          if (user == null) {
            return const LoginScreen();
          }

          return FutureBuilder<bool>(
            future: FirebaseService.isCurrentUserAdminOperator(
              forceRefresh: true,
            ),
            builder: (context, operatorSnapshot) {
              if (operatorSnapshot.connectionState != ConnectionState.done) {
                return const Scaffold(
                  body: Center(
                    child: CircularProgressIndicator(),
                  ),
                );
              }

              if (operatorSnapshot.data == true) {
                return const OpsDashboardScreen();
              }

              return const _OperatorAccessDeniedScreen();
            },
          );
        },
      ),
    );
  }
}

class _OperatorAccessDeniedScreen extends StatelessWidget {
  const _OperatorAccessDeniedScreen();

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 460),
          child: Card(
            margin: const EdgeInsets.all(24),
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Text(
                    'Access denied',
                    style: TextStyle(fontSize: 22, fontWeight: FontWeight.bold),
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 12),
                  const Text(
                    'This console requires Firebase Auth claim role=admin_operator '
                    'or admin_operator=true.',
                    textAlign: TextAlign.center,
                  ),
                  const SizedBox(height: 16),
                  FilledButton.icon(
                    onPressed: FirebaseService.signOut,
                    icon: const Icon(Icons.logout),
                    label: const Text('Logout'),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
