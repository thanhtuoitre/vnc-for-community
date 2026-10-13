package com.thanhvnc.client.protocol

import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

object HmacAuth {
    fun computeHmac(challenge: ByteArray, password: String): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        val key = SecretKeySpec(password.toByteArray(Charsets.UTF_8), "HmacSHA256")
        mac.init(key)
        return mac.doFinal(challenge)
    }
}
