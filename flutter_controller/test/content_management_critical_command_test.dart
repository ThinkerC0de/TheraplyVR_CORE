import 'package:flutter_controller/models/content_delivery_contract.dart';
import 'package:flutter_controller/models/critical_command_envelope.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('content management commands use critical ACK/NACK path', () {
    expect(
      CriticalCommandIds.isCritical(ContentDeliveryCommandIds.syncCatalog),
      isTrue,
    );
    expect(
      CriticalCommandIds.isCritical(ContentDeliveryCommandIds.installGame),
      isTrue,
    );
    expect(
      CriticalCommandIds.isCritical(ContentDeliveryCommandIds.uninstallGame),
      isTrue,
    );
  });
}
