# 📱 Flutter Controller App

**Theraply VR Controller - Ready to Run!**

---

## 🚀 Quick Start

### **1. Install Dependencies**
```bash
cd C:\Users\licen\Projects\theraply-vr-framework\flutter_controller
flutter pub get
```

### **2. Run on Emulator (Test)**
```bash
flutter run
```

### **3. Build APK for Phone**
```bash
flutter build apk
```

APK location: `build/app/outputs/flutter-apk/app-release.apk`

### **4. Install on Phone**
```bash
# Via USB
adb install build/app/outputs/flutter-apk/app-release.apk

# Or copy APK to phone and install manually
```

---

## 🔥 Firebase Setup (Required!)

### **Before running, you need:**

1. **Create Firebase project**
   - Go to https://console.firebase.google.com
   - Create project: `theraply-vr-demo`

2. **Enable Authentication**
   - Authentication → Sign-in method → Email/Password → Enable
   - Add test user: `therapist@test.com` / `Test123!`

3. **Enable Firestore**
   - Firestore Database → Create database → Test mode

4. **Download google-services.json**
   - Project Settings → Add Android app
   - Package name: `com.yourcompany.flutter_controller`
   - Download `google-services.json`
   - **IMPORTANT:** Place here:
     ```
     android/app/google-services.json
     ```

5. **Run again**
   ```bash
   flutter run
   ```

---

## ✅ App Features

### **Login Screen**
- Default credentials: `therapist@test.com` / `Test123!`
- Firebase authentication
- Auto-filled for testing

### **Scanner Screen**
- UDP discovery on port 8767
- Finds Quest devices on same WiFi
- Shows device name + IP

### **Control Screen**
- TCP connection to Quest
- START/STOP/RESET buttons
- Connection status indicator
- Real-time messaging
- Live WebRTC video preview from Quest (16:9)
- Automatic reconnect after app resume/background interruption
- Android foreground session support during active control session

---

## 🧪 Testing Without Quest

You can test the app UI without Quest:

1. Run app on emulator
2. Login works (connects to Firebase)
3. Scanner shows "No devices found" (expected - no Quest)
4. UI is fully functional

**To test with Quest:**
- Quest and Phone must be on same WiFi
- Unity app must be running on Quest
- Quest will broadcast UDP discovery

---

## 🐛 Troubleshooting

### **Error: "Failed to initialize Firebase"**
```
Solution: Make sure google-services.json is in android/app/
```

### **Error: "No devices found"**
```
Solution: 
1. Quest and Phone on same WiFi?
2. Unity app running on Quest?
3. Check Quest is broadcasting (port 8767)
```

### **Error: "Connection failed"**
```
Solution:
1. Check IP address is correct
2. Check port 8080 is open
3. Quest app is listening on TCP
```

### **Video preview freezes after app resume**
```
Expected behavior:
1. TCP reconnect starts automatically on app resume
2. WebRTC signaling is renegotiated
3. Preview recovers automatically

If not recovered:
- Check Unity logs for reconnect + WEBRTC_OFFER
- Check controller logs for WEBRTC_ANSWER and ICE exchange
```

### **Build errors**
```bash
# Clean and rebuild
flutter clean
flutter pub get
flutter run
```

---

## 📊 Project Structure

```
flutter_controller/
├── lib/
│   ├── main.dart                 # App entry point
│   ├── screens/
│   │   ├── login_screen.dart     # Firebase login
│   │   ├── scanner_screen.dart   # UDP device discovery
│   │   └── control_screen.dart   # TCP game controls
│   ├── services/
│   │   ├── firebase_service.dart # Auth + Firestore
│   │   ├── discovery_service.dart # UDP scanning
│   │   ├── connection_service.dart # TCP connection + reconnect
│   │   ├── webrtc_media_service.dart # WebRTC signaling + media handling
│   │   └── foreground_service_bridge.dart # Android foreground service bridge
│   └── models/
│       ├── device_info.dart      # Quest device model
│       └── game_status.dart      # Game state model
├── android/
│   └── app/
│       ├── build.gradle          # Android config
│       └── google-services.json  # ← ADD THIS FILE!
└── pubspec.yaml                  # Dependencies
```

---

## 🎯 Next Steps

After app runs successfully:

1. ✅ **Setup Unity** - Build Quest app with networking
2. ✅ **Same WiFi** - Quest + Phone on same network
3. ✅ **Test Discovery** - Scanner should find Quest
4. ✅ **Test Connection** - Connect and send commands
5. ✅ **Test Firebase** - Check data in Firestore console

---

## 💡 Tips

- **Default login** is pre-filled for quick testing
- **UDP port 8767** must match Unity's discovery port
- **TCP port 8080** must match Unity's control port
- **Control Screen keeps screen awake** (wakelock enabled while active)
- **Foreground service stops automatically** when app task is removed from recents

---

**App is ready! Just add google-services.json and run!** 🚀
