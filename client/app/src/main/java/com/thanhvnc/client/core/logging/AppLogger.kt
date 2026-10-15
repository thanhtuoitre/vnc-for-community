package com.thanhvnc.client.core.logging

import android.content.Context
import android.util.Log
import java.io.File
import java.io.FileWriter
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

object AppLogger {
    private const val TAG = "ThanhVNC"
    private val dateFormat = SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.US)
    private var logFile: File? = null
    private val lock = Any()
    private const val MAX_LOG_SIZE_BYTES = 2 * 1024 * 1024 // 2 MB

    fun init(context: Context) {
        synchronized(lock) {
            try {
                val logDir = File(context.getExternalFilesDir(null) ?: context.filesDir, "logs")
                if (!logDir.exists()) {
                    logDir.mkdirs()
                }
                logFile = File(logDir, "client.log")
            } catch (e: Exception) {
                Log.e(TAG, "Failed to initialize AppLogger file", e)
            }
        }
    }

    fun d(message: String) {
        Log.d(TAG, message)
        appendToFile("DEBUG", message, null)
    }

    fun i(message: String) {
        Log.i(TAG, message)
        appendToFile("INFO", message, null)
    }

    fun w(message: String, throwable: Throwable? = null) {
        Log.w(TAG, message, throwable)
        appendToFile("WARN", message, throwable)
    }

    fun e(message: String, throwable: Throwable? = null) {
        Log.e(TAG, message, throwable)
        appendToFile("ERROR", message, throwable)
    }

    private fun appendToFile(level: String, message: String, throwable: Throwable?) {
        val file = logFile ?: return
        synchronized(lock) {
            try {
                rotateIfNeeded(file)
                val timestamp = dateFormat.format(Date())
                val thread = Thread.currentThread().name
                FileWriter(file, true).use { writer ->
                    writer.write("[$timestamp] [$level] [$thread] $message\n")
                    throwable?.let {
                        writer.write(Log.getStackTraceString(it))
                        writer.write("\n")
                    }
                }
            } catch (_: Exception) {
                // Do not throw on logging errors
            }
        }
    }

    private fun rotateIfNeeded(file: File) {
        if (file.exists() && file.length() >= MAX_LOG_SIZE_BYTES) {
            val archive = File(file.parentFile, "client.1.log")
            if (archive.exists()) {
                archive.delete()
            }
            file.renameTo(archive)
        }
    }
}
