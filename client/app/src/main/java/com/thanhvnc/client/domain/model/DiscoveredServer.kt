package com.thanhvnc.client.domain.model

data class DiscoveredServer(
    val name: String,
    val host: String,
    val port: Int,
    val useTls: Boolean,
    val fingerprint: String? = null,
    val lastSeenMs: Long = System.currentTimeMillis()
)
