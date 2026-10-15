package com.thanhvnc.client.domain.usecase

import com.thanhvnc.client.model.ServerProfile
import com.thanhvnc.client.model.ServerProfileRepository

class ManageProfilesUseCase(private val repository: ServerProfileRepository) {
    fun getAllProfiles(): List<ServerProfile> = repository.getAllProfiles()

    fun saveProfile(profile: ServerProfile) {
        repository.saveProfile(profile)
    }

    fun deleteProfile(id: String) {
        repository.deleteProfile(id)
    }

    fun recordConnection(id: String) {
        repository.updateLastConnected(id)
    }
}
