import 'package:flutter/material.dart';
import 'package:flutter_controller/services/student_service.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:flutter_controller/services/entitlement_service.dart';
import 'package:flutter_controller/screens/students_screen.dart';

class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  static const String _lastLoginEmailKey = 'last_login_email_v1';
  static const String _lastLoginUserIdKey = 'last_login_user_id_v1';

  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();
  bool _isLoading = false;
  bool _isRestoring = true;
  String? _error;
  String? _entitlementReasonCode;

  @override
  void initState() {
    super.initState();
    _restoreLastLoginEmail();
  }

  Future<void> _restoreLastLoginEmail() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final lastEmail = prefs.getString(_lastLoginEmailKey);
      if (!mounted) {
        return;
      }

      if (lastEmail != null && lastEmail.trim().isNotEmpty) {
        _emailController.text = lastEmail.trim();
      }
    } catch (_) {
      // Ignore local cache restore errors, login can still continue manually.
    } finally {
      if (mounted) {
        setState(() {
          _isRestoring = false;
        });
      }
    }
  }

  Future<void> _persistLastLoginEmail(String email) async {
    final normalized = email.trim();
    if (normalized.isEmpty) {
      return;
    }

    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_lastLoginEmailKey, normalized);
  }

  Future<void> _handleAccountSwitchIfNeeded(String currentUserId) async {
    final normalized = currentUserId.trim();
    if (normalized.isEmpty) {
      return;
    }

    final prefs = await SharedPreferences.getInstance();
    final previousUserId = prefs.getString(_lastLoginUserIdKey)?.trim();
    final didSwitchAccount = previousUserId != null &&
        previousUserId.isNotEmpty &&
        previousUserId != normalized;

    if (didSwitchAccount) {
      await StudentService.clearSessionState();
      EntitlementService.clearSessionAccess();
    }

    await prefs.setString(_lastLoginUserIdKey, normalized);
  }

  @override
  void dispose() {
    _emailController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _handleLogin() async {
    setState(() {
      _isLoading = true;
      _error = null;
      _entitlementReasonCode = null;
    });

    try {
      final user = await FirebaseService.signIn(
        _emailController.text.trim(),
        _passwordController.text,
      );

      if (user != null) {
        await _persistLastLoginEmail(_emailController.text);
        await _handleAccountSwitchIfNeeded(user.uid);
        final didBootstrapEntitlement =
            await EntitlementService.tryBootstrapDevelopmentEntitlement(user);
        final gateDecision = await EntitlementService.evaluateLoginGate(user);

        if (!mounted) {
          return;
        }

        if (!gateDecision.isAllowed) {
          await FirebaseService.signOut();
          setState(() {
            _error = gateDecision.message;
            _entitlementReasonCode = gateDecision.reasonCode;
          });
          return;
        }

        if (gateDecision.usedLegacyFallback) {
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text(
                '${gateDecision.message} (${gateDecision.reasonCode})',
              ),
            ),
          );
        }

        if (didBootstrapEntitlement) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(
              content: Text(
                'Development entitlement profile bootstrap created for this user.',
              ),
              duration: Duration(seconds: 2),
            ),
          );
        }

        Navigator.pushReplacement(
          context,
          MaterialPageRoute(builder: (context) => const StudentsScreen()),
        );
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _error = e.toString();
          _entitlementReasonCode = null;
        });
      }
    } finally {
      if (mounted) {
        setState(() {
          _isLoading = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24.0),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Icon(
                Icons.psychology,
                size: 80,
                color: Colors.blue,
              ),
              const SizedBox(height: 16),
              const Text(
                'Theraply VR',
                style: TextStyle(fontSize: 32, fontWeight: FontWeight.bold),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 8),
              Text(
                'Controller App',
                style: TextStyle(fontSize: 18, color: Colors.grey[600]),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 48),
              TextField(
                controller: _emailController,
                decoration: const InputDecoration(
                  labelText: 'Email',
                  border: OutlineInputBorder(),
                  prefixIcon: Icon(Icons.email),
                ),
                keyboardType: TextInputType.emailAddress,
                enabled: !_isLoading && !_isRestoring,
              ),
              const SizedBox(height: 16),
              TextField(
                controller: _passwordController,
                decoration: const InputDecoration(
                  labelText: 'Password',
                  border: OutlineInputBorder(),
                  prefixIcon: Icon(Icons.lock),
                ),
                obscureText: true,
                enabled: !_isLoading && !_isRestoring,
                onSubmitted: (_) => _handleLogin(),
              ),
              const SizedBox(height: 24),
              if (_error != null)
                Padding(
                  padding: const EdgeInsets.only(bottom: 16),
                  child: _buildErrorCard(),
                ),
              ElevatedButton(
                onPressed: (_isLoading || _isRestoring) ? null : _handleLogin,
                style: ElevatedButton.styleFrom(
                  padding: const EdgeInsets.symmetric(vertical: 16),
                ),
                child: _isLoading
                    ? const SizedBox(
                        height: 20,
                        width: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Text('Login', style: TextStyle(fontSize: 16)),
              ),
              const SizedBox(height: 16),
              Text(
                _isRestoring
                    ? 'Restoring last login email...'
                    : 'Last used email is remembered on this device',
                style: TextStyle(fontSize: 12, color: Colors.grey[600]),
                textAlign: TextAlign.center,
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildErrorCard() {
    final reasonCode = _entitlementReasonCode;
    final errorText = _error ?? '';

    String title = 'Login failed';
    String nextStep = 'Check credentials and try again.';

    switch (reasonCode) {
      case 'ENTITLEMENT_RECORD_REQUIRED':
        title = 'Access profile missing';
        nextStep =
            'Ask an admin operator to create your entitlement profile, then log in again.';
        break;
      case 'APP_LICENSE_INACTIVE':
        title = 'App license inactive';
        nextStep =
            'Ask an admin operator to grant app access or reactivate the license.';
        break;
      case 'ENTITLEMENT_BACKEND_UNAVAILABLE':
        title = 'Entitlement backend unavailable';
        nextStep =
            'Check network access and retry. If this persists, contact support.';
        break;
    }

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.red.shade50,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: Colors.red.shade200),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            style: TextStyle(
              color: Colors.red.shade800,
              fontWeight: FontWeight.w700,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            errorText,
            style: TextStyle(color: Colors.red.shade800),
          ),
          const SizedBox(height: 6),
          Text(
            'Next step: $nextStep',
            style: TextStyle(color: Colors.red.shade700, fontSize: 12),
          ),
          if (reasonCode != null) ...[
            const SizedBox(height: 4),
            Text(
              'Code: $reasonCode',
              style: TextStyle(color: Colors.red.shade600, fontSize: 11),
            ),
          ],
        ],
      ),
    );
  }
}
