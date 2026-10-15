package com.thanhvnc.client.model

import org.json.JSONObject
import java.util.UUID

data class ServerProfile(
    val id: String = UUID.randomUUID().toString(),
    var name: String,
    var host: String,
    var port: Int = 5901,
    var password: String = "",
    var lastConnectedMs: Long = 0L,
    var useTls: Boolean = true,
    var expectedFingerprint: String = ""
) {
    fun toJson(): JSONObject {
        return JSONObject().apply {
            put("id", id)
            put("name", name)
            put("host", host)
            put("port", port)
            put("password", password)
            put("lastConnectedMs", lastConnectedMs)
            put("useTls", useTls)
            put("expectedFingerprint", expectedFingerprint)
        }
    }

    companion object {
        fun fromJson(json: JSONObject): ServerProfile {
            return ServerProfile(
                id = json.optString("id", UUID.randomUUID().toString()),
                name = json.optString("name", "Remote Desktop"),
                host = json.optString("host", "127.0.0.1"),
                port = json.optInt("port", 5901),
                password = json.optString("password", ""),
                lastConnectedMs = json.optLong("lastConnectedMs", 0L),
                useTls = json.optBoolean("useTls", true),
                expectedFingerprint = json.optString("expectedFingerprint", "")
            )
        }
    }
}
