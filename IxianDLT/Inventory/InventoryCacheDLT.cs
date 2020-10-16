﻿using DLT.Meta;
using DLT.Network;
using IXICore;
using IXICore.Inventory;
using IXICore.Meta;
using IXICore.Network;
using IXICore.Utils;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace DLTNode.Inventory
{
    class InventoryCacheDLT : InventoryCache
    {
        public InventoryCacheDLT():base()
        {

        }

        override protected bool sendInventoryRequest(InventoryItem item, RemoteEndpoint endpoint)
        {
            switch (item.type)
            {
                case InventoryItemTypes.block:
                    return handleBlock(item, endpoint);
                case InventoryItemTypes.blockSignature:
                    return handleSignature(item, endpoint);
                case InventoryItemTypes.keepAlive:
                    return handleKeepAlive(item, endpoint);
                case InventoryItemTypes.transaction:
                    CoreProtocolMessage.broadcastGetTransaction(UTF8Encoding.UTF8.GetString(item.hash), 0, endpoint);
                    return true;
            }
            return false;
        }

        private bool handleBlock(InventoryItem item, RemoteEndpoint endpoint)
        {
            InventoryItemBlock iib = (InventoryItemBlock)item;
            ulong last_block_height = IxianHandler.getLastBlockHeight();
            if (iib.blockNum > last_block_height)
            {
                byte include_tx = 2;
                if(Node.isMasterNode())
                {
                    include_tx = 0;
                }
                ProtocolMessage.broadcastGetBlock(last_block_height + 1, null, endpoint, include_tx, true);
                return true;
            }
            return false;
        }

        private bool handleKeepAlive(InventoryItem item, RemoteEndpoint endpoint)
        {
            InventoryItemKeepAlive iika = (InventoryItemKeepAlive)item;
            Presence p = PresenceList.getPresenceByAddress(iika.address);
            if (p == null)
            {
                using (MemoryStream mw = new MemoryStream())
                {
                    using (BinaryWriter writer = new BinaryWriter(mw))
                    {
                        writer.Write(iika.address.Length);
                        writer.Write(iika.address);

                        endpoint.sendData(ProtocolMessageCode.getPresence, mw.ToArray(), null);
                    }
                }
                return true;
            }
            else
            {
                var pa = p.addresses.Find(x => x.device.SequenceEqual(iika.deviceId));
                if (pa == null || iika.lastSeen > pa.lastSeenTime)
                {
                    byte[] address_len_bytes = ((ulong)iika.address.Length).GetVarIntBytes();
                    byte[] device_len_bytes = ((ulong)iika.deviceId.Length).GetVarIntBytes();
                    byte[] data = new byte[address_len_bytes.Length + iika.address.Length + device_len_bytes.Length + iika.deviceId.Length];
                    Array.Copy(address_len_bytes, data, address_len_bytes.Length);
                    Array.Copy(iika.address, 0, data, address_len_bytes.Length, iika.address.Length);
                    Array.Copy(device_len_bytes, 0, data, address_len_bytes.Length + iika.address.Length, device_len_bytes.Length);
                    Array.Copy(iika.deviceId, 0, data, address_len_bytes.Length + iika.address.Length + device_len_bytes.Length, iika.deviceId.Length);
                    endpoint.sendData(ProtocolMessageCode.getKeepAlive, data, null);
                    return true;
                }
            }
            return false;
        }

        private bool handleSignature(InventoryItem item, RemoteEndpoint endpoint)
        {
            InventoryItemSignature iis = (InventoryItemSignature)item;
            ulong last_block_height = IxianHandler.getLastBlockHeight();
            byte[] address = iis.address;
            ulong block_num = iis.blockNum;
            if (block_num + 5 > last_block_height && block_num <= last_block_height + 1)
            {
                if (block_num == last_block_height + 1)
                {
                    lock (Node.blockProcessor.localBlockLock)
                    {
                        Block local_block = Node.blockProcessor.localNewBlock;
                        if (local_block == null || local_block.blockNum != block_num)
                        {
                            return false;
                        }
                        if (!local_block.blockChecksum.SequenceEqual(iis.blockHash)
                            || local_block.hasNodeSignature(address))
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    Block sf_block = Node.blockChain.getBlock(block_num);
                    if (!sf_block.blockChecksum.SequenceEqual(iis.blockHash)
                        || sf_block.hasNodeSignature(address))
                    {
                        return false;
                    }
                }
                byte[] block_num_bytes = block_num.GetVarIntBytes();
                byte[] addr_len_bytes = ((ulong)address.Length).GetVarIntBytes();
                byte[] data = new byte[block_num_bytes.Length + addr_len_bytes.Length + address.Length];
                Array.Copy(block_num_bytes, data, block_num_bytes.Length);
                Array.Copy(addr_len_bytes, 0, data, block_num_bytes.Length, addr_len_bytes.Length);
                Array.Copy(address, 0, data, block_num_bytes.Length + addr_len_bytes.Length, address.Length);
                endpoint.sendData(ProtocolMessageCode.getBlockSignature, data, null);
                return true;
            }
            return false;
        }
    }
}