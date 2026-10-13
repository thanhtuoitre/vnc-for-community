package com.thanhvnc.client.model

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

class ServerProfileRepository(context: Context) {

    private val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    fun getAllProfiles(): List<ServerProfile> {
        val jsonString = prefs.getString(KEY_PROFILES, null) ?: return defaultProfiles()
        return try {
            val jsonArray = JSONArray(jsonString)
            val list = mutableListOf<ServerProfile>()
            for (i in 0 until jsonArray.length()) {
                val obj = jsonArray.getJSONObject(i)
                list.add(ServerProfile.fromJson(obj))
            }
            if (list.isEmpty()) defaultProfiles() else list
        } catch (_: Exception) {
            defaultProfiles()
        }
    }

    fun saveProfile(profile: ServerProfile) {
        val current = getAllProfiles().toMutableList()
        val index = current.indexOfFirst { it.id == profile.id }
        if (index >= 0) {
            current[index] = profile
        } else {
            current.add(0, profile)
        }
        persist(current)
    }

    fun deleteProfile(id: String) {
        val current = getAllProfiles().filterNot { it.id == id }
        persist(current)
    }

    fun updateLastConnected(id: String) {
        val current = getAllProfiles().toMutableList()
        val index = current.indexOfFirst { it.id == id }
        if (index >= 0) {
            current[index].lastConnectedMs = System.currentTimeMillis()
            persist(current)
        }
    }

    private fun persist(list: List<ServerProfile>) {
        val jsonArray = JSONArray()
        list.forEach { jsonArray.put(it.toJson()) }
        prefs.edit().putString(KEY_PROFILES, jsonArray.toString()).apply()
    }

    private fun defaultProfiles(): List<ServerProfile> {
        return listOf(
            ServerProfile(
                name = "Local Windows PC",
                host = "192.168.1.10",
                port = 5901,
                password = "vncsecret"
            )
        )
    }

    companion object {
        private const val PREFS_NAME = "thanhvnc_servers"
        private const val KEY_PROFILES = "profiles_json"
    }
}
