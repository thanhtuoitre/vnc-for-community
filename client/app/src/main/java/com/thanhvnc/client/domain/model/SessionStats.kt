package com.thanhvnc.client.domain.model

data class SessionStats(
    val fps: Int = 0,
    val rttMs: Long = 0L,
    val framesReceived: Long = 0L,
    val isAdaptiveThrottled: Boolean = false,
    val qualityPercent: Int = 70,
    val targetFps: Int = 30
)
