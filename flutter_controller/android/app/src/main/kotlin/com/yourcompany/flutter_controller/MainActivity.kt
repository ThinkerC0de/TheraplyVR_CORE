package com.yourcompany.flutter_controller

import android.content.Intent
import androidx.core.content.ContextCompat
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    private val channelName = "theraply/foreground_service"

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)

        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, channelName)
            .setMethodCallHandler { call, result ->
                when (call.method) {
                    "start" -> {
                        startConnectionForegroundService()
                        result.success(true)
                    }

                    "stop" -> {
                        stopConnectionForegroundService()
                        result.success(true)
                    }

                    else -> result.notImplemented()
                }
            }
    }

    private fun startConnectionForegroundService() {
        val intent = Intent(this, ConnectionForegroundService::class.java).apply {
            action = ConnectionForegroundService.ACTION_START
        }
        ContextCompat.startForegroundService(this, intent)
    }

    private fun stopConnectionForegroundService() {
        val intent = Intent(this, ConnectionForegroundService::class.java).apply {
            action = ConnectionForegroundService.ACTION_STOP
        }
        startService(intent)
    }
}
