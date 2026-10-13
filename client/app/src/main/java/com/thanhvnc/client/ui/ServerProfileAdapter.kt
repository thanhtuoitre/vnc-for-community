package com.thanhvnc.client.ui

import android.view.LayoutInflater
import android.view.ViewGroup
import android.widget.PopupMenu
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.thanhvnc.client.R
import com.thanhvnc.client.databinding.ItemServerProfileBinding
import com.thanhvnc.client.model.ServerProfile

class ServerProfileAdapter(
    private val onConnect: (ServerProfile) -> Unit,
    private val onEdit: (ServerProfile) -> Unit,
    private val onDelete: (ServerProfile) -> Unit
) : ListAdapter<ServerProfile, ServerProfileAdapter.ProfileViewHolder>(DiffCallback) {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ProfileViewHolder {
        val binding = ItemServerProfileBinding.inflate(
            LayoutInflater.from(parent.context),
            parent,
            false
        )
        return ProfileViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ProfileViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    inner class ProfileViewHolder(
        private val binding: ItemServerProfileBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(profile: ServerProfile) {
            binding.textProfileName.text = profile.name
            binding.textProfileHost.text = "${profile.host}:${profile.port}"

            val lastSeenText = if (profile.lastConnectedMs > 0) {
                val diffMin = (System.currentTimeMillis() - profile.lastConnectedMs) / (1000 * 60)
                when {
                    diffMin < 2 -> "Connected just now"
                    diffMin < 60 -> "Connected ${diffMin}m ago"
                    diffMin < 1440 -> "Connected ${diffMin / 60}h ago"
                    else -> "Connected ${(diffMin / 1440)}d ago"
                }
            } else {
                "Ready to connect"
            }
            binding.textLastSeen.text = lastSeenText

            binding.btnConnectProfile.setOnClickListener {
                onConnect(profile)
            }

            binding.root.setOnClickListener {
                onConnect(profile)
            }

            binding.btnProfileMenu.setOnClickListener { view ->
                val popup = PopupMenu(view.context, view)
                popup.menu.add(0, 1, 0, R.string.edit_server)
                popup.menu.add(0, 2, 1, R.string.delete_server)
                popup.setOnMenuItemClickListener { item ->
                    when (item.itemId) {
                        1 -> { onEdit(profile); true }
                        2 -> { onDelete(profile); true }
                        else -> false
                    }
                }
                popup.show()
            }
        }
    }

    companion object DiffCallback : DiffUtil.ItemCallback<ServerProfile>() {
        override fun areItemsTheSame(oldItem: ServerProfile, newItem: ServerProfile): Boolean =
            oldItem.id == newItem.id

        override fun areContentsTheSame(oldItem: ServerProfile, newItem: ServerProfile): Boolean =
            oldItem == newItem
    }
}
