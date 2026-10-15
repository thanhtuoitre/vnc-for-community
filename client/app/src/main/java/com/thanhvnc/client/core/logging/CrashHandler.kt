package com.thanhvnc.client.core.logging

import android.content.Context
import android.os.Build
import android.util.Log
import java.io.File
import java.io.FileWriter
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class CrashHandler(private val context: Context) : Thread.UncaughtExceptionHandler {
    private val defaultHandler = Thread.getDefaultUncaughtExceptionHandler()
    private val dateFormat = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US)

    companion object {
        fun install(context: Context) {
            val handler = CrashHandler(context)
            Thread.setDefaultUncaughtExceptionHandler(handler)
        }
    }

    override fun uncaughtException(thread: Thread, throwable: Throwable) {
        try {
            val crashDir = File(context.getExternalFilesDir(null) ?: context.filesDir, "crashes")
            if (!crashDir.exists()) {
                crashDir.mkdirs()
            }
            val timestamp = dateFormat.format(Date())
            val crashFile = File(crashDir, "crash_$timestamp.txt")

            FileWriter(crashFile).use { writer ->
                writer.write("================================================================================\n")
                writer.write("ThanhVNC Android Client Crash Report\n")
                writer.write("Timestamp: ${Date()}\n")
                writer.write("Thread: ${thread.name} (id: ${thread.id})\n")
                writer.write("Device: ${Build.MANUFACTURER} ${Build.MODEL} (${Build.DEVICE})\n")
                writer.write("Android OS: ${Build.VERSION.RELEASE} (SDK ${Build.VERSION.SDK_INT})\n")
                writer.write("================================================================================\n")
                writer.write(Log.getStackTraceString(throwable))
                writer.write("\n================================================================================\n")
            }
            AppLogger.e("Uncaught exception written to ${crashFile.absolutePath}", throwable)
        } catch (e: Exception) {
            Log.e("CrashHandler", "Failed to write crash dump", e)
        } finally {
            defaultHandler?.uncaughtException(thread, throwable)
        }
    }
}
