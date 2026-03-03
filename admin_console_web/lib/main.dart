import 'package:flutter/material.dart';
import 'package:firebase_auth/firebase_auth.dart';
import 'package:admin_console_web/screens/login_screen.dart';
import 'package:admin_console_web/screens/ops_dashboard_screen.dart';
import 'package:admin_console_web/services/firebase_service.dart';
import 'package:google_fonts/google_fonts.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await FirebaseService.initialize();
  runApp(const AdminConsoleApp());
}

class AdminConsoleApp extends StatelessWidget {
  const AdminConsoleApp({super.key});

  @override
  Widget build(BuildContext context) {
    const panelBorderColor = Color(0xFFCCD7E7);
    const accentCyan = Color(0xFF0F8B8D);
    const accentAmber = Color(0xFFF59E0B);
    final baseTextTheme = GoogleFonts.manropeTextTheme();
    final elevatedTextTheme = baseTextTheme.apply(
      bodyColor: const Color(0xFF0F172A),
      displayColor: const Color(0xFF0F172A),
    );

    return MaterialApp(
      title: 'Theraply Admin Console',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: const ColorScheme.light(
          primary: accentCyan,
          secondary: accentAmber,
          surface: Color(0xFFF8FBFF),
          onSurface: Color(0xFF0F172A),
          error: Color(0xFFB42318),
        ),
        useMaterial3: true,
        textTheme: elevatedTextTheme,
        scaffoldBackgroundColor: Colors.transparent,
        appBarTheme: const AppBarTheme(
          backgroundColor: Color(0xFF0F253A),
          foregroundColor: Colors.white,
          elevation: 0,
          scrolledUnderElevation: 0,
          centerTitle: false,
          titleTextStyle: TextStyle(
            fontSize: 22,
            fontWeight: FontWeight.w800,
            letterSpacing: 0.2,
            color: Colors.white,
          ),
        ),
        cardTheme: CardThemeData(
          color: const Color(0xFFF8FBFF),
          elevation: 2,
          margin: EdgeInsets.zero,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(18),
            side: const BorderSide(color: panelBorderColor),
          ),
        ),
        inputDecorationTheme: InputDecorationTheme(
          filled: true,
          fillColor: const Color(0xFFF9FCFF),
          border: OutlineInputBorder(
            borderRadius: BorderRadius.circular(12),
          ),
          enabledBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(12),
            borderSide: const BorderSide(color: panelBorderColor),
          ),
          focusedBorder: OutlineInputBorder(
            borderRadius: BorderRadius.circular(12),
            borderSide: const BorderSide(
              color: accentCyan,
              width: 1.6,
            ),
          ),
        ),
        filledButtonTheme: FilledButtonThemeData(
          style: FilledButton.styleFrom(
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(12),
            ),
            backgroundColor: const Color(0xFF0F8B8D),
            foregroundColor: Colors.white,
            textStyle: const TextStyle(fontWeight: FontWeight.w700),
          ),
        ),
        outlinedButtonTheme: OutlinedButtonThemeData(
          style: OutlinedButton.styleFrom(
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(12),
            ),
            side: const BorderSide(color: Color(0xFF88A6C8)),
            foregroundColor: const Color(0xFF0F3555),
          ),
        ),
        tabBarTheme: const TabBarThemeData(
          dividerColor: Colors.transparent,
          labelColor: Colors.white,
          unselectedLabelColor: Color(0xFFBFD3EB),
          indicatorColor: Color(0xFFF59E0B),
          indicatorSize: TabBarIndicatorSize.label,
        ),
        chipTheme: ThemeData.light().chipTheme.copyWith(
              shape: const StadiumBorder(),
              side: const BorderSide(color: panelBorderColor),
            ),
      ),
      builder: (context, child) {
        return DecoratedBox(
          decoration: const BoxDecoration(
            gradient: LinearGradient(
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
              colors: [
                Color(0xFFEAF3FF),
                Color(0xFFF4FAFF),
                Color(0xFFE7F8F6),
              ],
            ),
          ),
          child: child ?? const SizedBox.shrink(),
        );
      },
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
