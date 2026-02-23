import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_controller/models/ops_error_catalog.dart';
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
  static const String _rememberPasswordKey = 'remember_password_v1';
  static const String _rememberedPasswordValueKey =
      'remembered_password_value_v1';
  static const FlutterSecureStorage _secureStorage = FlutterSecureStorage();

  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();
  bool _isLoading = false;
  bool _isRestoring = true;
  bool _rememberPassword = false;
  bool _obscurePassword = true;
  bool _passwordRestoredFromStorage = false;
  bool _passwordEditedManually = false;
  String? _error;
  String? _entitlementReasonCode;

  @override
  void initState() {
    super.initState();
    _restoreLastLoginPreferences();
  }

  Future<void> _restoreLastLoginPreferences() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final lastEmail = prefs.getString(_lastLoginEmailKey);
      final rememberPassword = prefs.getBool(_rememberPasswordKey) ?? false;
      var rememberedPassword =
          await _secureStorage.read(key: _rememberedPasswordValueKey) ?? '';
      final legacyRememberedPassword =
          prefs.getString(_rememberedPasswordValueKey) ?? '';
      if (!mounted) {
        return;
      }

      if (mounted) {
        setState(() {
          _rememberPassword = rememberPassword;
        });
      }

      if (lastEmail != null && lastEmail.trim().isNotEmpty) {
        _emailController.text = lastEmail.trim();
      }

      // Migrate historical plaintext password from SharedPreferences.
      if (rememberedPassword.isEmpty && legacyRememberedPassword.isNotEmpty) {
        rememberedPassword = legacyRememberedPassword;
        await _secureStorage.write(
          key: _rememberedPasswordValueKey,
          value: legacyRememberedPassword,
        );
      }
      if (legacyRememberedPassword.isNotEmpty) {
        await prefs.remove(_rememberedPasswordValueKey);
      }

      if (rememberPassword && rememberedPassword.isNotEmpty) {
        _passwordController.text = rememberedPassword;
        _passwordRestoredFromStorage = true;
        _passwordEditedManually = false;
        _obscurePassword = true;
      } else {
        await _secureStorage.delete(key: _rememberedPasswordValueKey);
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

  Future<void> _persistRememberedPasswordPreference(String password) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_rememberPasswordKey, _rememberPassword);

    if (_rememberPassword && password.isNotEmpty) {
      await _secureStorage.write(
        key: _rememberedPasswordValueKey,
        value: password,
      );
      await prefs.remove(_rememberedPasswordValueKey);
      return;
    }

    await _secureStorage.delete(key: _rememberedPasswordValueKey);
    await prefs.remove(_rememberedPasswordValueKey);
  }

  Future<void> _handleRememberPasswordChanged(bool value) async {
    setState(() {
      _rememberPassword = value;
    });

    if (value) {
      return;
    }

    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_rememberPasswordKey, false);
    await _secureStorage.delete(key: _rememberedPasswordValueKey);
    await prefs.remove(_rememberedPasswordValueKey);
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
    final email = _emailController.text.trim();
    final password = _passwordController.text;

    if (email.isEmpty || password.isEmpty) {
      setState(() {
        _error = 'Email and password are required.';
        _entitlementReasonCode = null;
      });
      return;
    }

    setState(() {
      _isLoading = true;
      _error = null;
      _entitlementReasonCode = null;
    });

    try {
      final user = await FirebaseService.signIn(email, password);

      if (user != null) {
        await _persistLastLoginEmail(email);
        await _persistRememberedPasswordPreference(password);
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
          final reasonTag =
              OpsErrorCatalog.buildReasonTag(gateDecision.reasonCode);
          ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(
              content: Text(
                '${gateDecision.message} [$reasonTag]',
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
    final busy = _isLoading || _isRestoring;
    final hasPasswordText = _passwordController.text.isNotEmpty;
    final canTogglePasswordVisibility =
        !_isLoading && hasPasswordText && _passwordEditedManually;
    final cachedEmail = _emailController.text.trim();
    final restorationLabel = _isRestoring
        ? 'Restoring login details...'
        : (cachedEmail.isEmpty
            ? 'No cached email on this device yet.'
            : (_rememberPassword
                ? 'Cached email: $cachedEmail (password remembered)'
                : 'Cached email: $cachedEmail'));

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 480),
            child: ListView(
              padding: const EdgeInsets.fromLTRB(20, 24, 20, 24),
              children: [
                Container(
                  padding: const EdgeInsets.all(20),
                  decoration: BoxDecoration(
                    color: Colors.blue.shade50,
                    borderRadius: BorderRadius.circular(14),
                    border: Border.all(color: Colors.blue.shade100),
                  ),
                  child: const Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Icon(
                        Icons.psychology,
                        size: 60,
                        color: Colors.blue,
                      ),
                      SizedBox(height: 12),
                      Text(
                        'Theraply VR',
                        style: TextStyle(
                          fontSize: 30,
                          fontWeight: FontWeight.bold,
                        ),
                        textAlign: TextAlign.center,
                      ),
                      SizedBox(height: 6),
                      Text(
                        'Controller App',
                        style: TextStyle(fontSize: 16),
                        textAlign: TextAlign.center,
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                Container(
                  padding:
                      const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                  decoration: BoxDecoration(
                    color: Colors.grey.shade100,
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: Row(
                    children: [
                      Icon(
                        _isRestoring ? Icons.autorenew : Icons.history,
                        size: 18,
                        color: Colors.grey.shade700,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          restorationLabel,
                          style: TextStyle(
                            fontSize: 12,
                            color: Colors.grey.shade700,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
                if (_isRestoring) ...[
                  const SizedBox(height: 8),
                  const LinearProgressIndicator(minHeight: 3),
                ],
                const SizedBox(height: 12),
                Container(
                  padding: const EdgeInsets.all(16),
                  decoration: BoxDecoration(
                    color: Colors.white,
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: Colors.grey.shade300),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const Text(
                        'Operator login',
                        style: TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: _emailController,
                        decoration: const InputDecoration(
                          labelText: 'Email',
                          border: OutlineInputBorder(),
                          prefixIcon: Icon(Icons.email),
                        ),
                        keyboardType: TextInputType.emailAddress,
                        enabled: !busy,
                      ),
                      const SizedBox(height: 12),
                      TextField(
                        controller: _passwordController,
                        decoration: InputDecoration(
                          labelText: 'Password',
                          border: const OutlineInputBorder(),
                          prefixIcon: const Icon(Icons.lock),
                          suffixIcon: IconButton(
                            tooltip: _obscurePassword
                                ? 'Show password'
                                : 'Hide password',
                            onPressed: canTogglePasswordVisibility
                                ? () {
                                    setState(() {
                                      _obscurePassword = !_obscurePassword;
                                    });
                                  }
                                : null,
                            icon: Icon(
                              _obscurePassword
                                  ? Icons.visibility
                                  : Icons.visibility_off,
                            ),
                          ),
                        ),
                        obscureText: _obscurePassword,
                        enabled: !busy,
                        enableSuggestions: false,
                        autocorrect: false,
                        onTap: () {
                          final text = _passwordController.text;
                          if (text.isEmpty) {
                            return;
                          }

                          _passwordController.selection = TextSelection(
                            baseOffset: 0,
                            extentOffset: text.length,
                          );
                        },
                        onSubmitted: (_) => _handleLogin(),
                        onChanged: (value) {
                          final shouldMarkEdited = value.isNotEmpty &&
                              (!_passwordEditedManually ||
                                  _passwordRestoredFromStorage);
                          if (!shouldMarkEdited) {
                            return;
                          }

                          setState(() {
                            _passwordEditedManually = true;
                            _passwordRestoredFromStorage = false;
                          });
                        },
                      ),
                      const SizedBox(height: 8),
                      Row(
                        children: [
                          Checkbox(
                            value: _rememberPassword,
                            onChanged: busy
                                ? null
                                : (value) => _handleRememberPasswordChanged(
                                      value ?? false,
                                    ),
                          ),
                          const SizedBox(width: 4),
                          const Expanded(
                            child: Text(
                              'Remember password on this device',
                              style: TextStyle(fontSize: 13),
                            ),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                if (_error != null) ...[
                  _buildErrorCard(),
                  const SizedBox(height: 12),
                ],
                SizedBox(
                  height: 48,
                  child: ElevatedButton.icon(
                    onPressed: busy ? null : _handleLogin,
                    icon: _isLoading
                        ? const SizedBox(
                            height: 18,
                            width: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.login),
                    label: Text(
                      _isLoading ? 'Signing in...' : 'Login',
                      style: const TextStyle(fontSize: 16),
                    ),
                  ),
                ),
                const SizedBox(height: 10),
                Text(
                  'Entitlement checks run after authentication. If access is denied, follow the next-step hint below the error.',
                  style: TextStyle(fontSize: 12, color: Colors.grey[600]),
                  textAlign: TextAlign.center,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildErrorCard() {
    final reasonCode = _entitlementReasonCode;
    final errorText = _error ?? '';
    final mappedReasonTag =
        reasonCode == null ? null : OpsErrorCatalog.buildReasonTag(reasonCode);

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
              'Code: $mappedReasonTag',
              style: TextStyle(color: Colors.red.shade600, fontSize: 11),
            ),
          ],
        ],
      ),
    );
  }
}
