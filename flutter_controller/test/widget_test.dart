import 'package:flutter_test/flutter_test.dart';

import 'package:flutter_controller/main.dart';

void main() {
  testWidgets('App renders login screen', (WidgetTester tester) async {
    await tester.pumpWidget(const TheraplyControllerApp());

    expect(find.text('Theraply VR'), findsOneWidget);
    expect(find.text('Login'), findsOneWidget);
  });
}
