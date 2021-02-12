﻿using Microsoft.Xna.Framework;
using System;
using System.Text;
using MoonSharp.Interpreter;

namespace Barotrauma.Networking
{
    partial class ChatMessage
    {
        public static void ServerRead(IReadMessage msg, Client c)
        {
            c.KickAFKTimer = 0.0f;

            UInt16 ID = msg.ReadUInt16();
            ChatMessageType type = (ChatMessageType)msg.ReadByte();
            string txt;

            Character orderTargetCharacter = null;
            Entity orderTargetEntity = null;
            OrderChatMessage orderMsg = null;
            OrderTarget orderTargetPosition = null;
            Order.OrderTargetType orderTargetType = Order.OrderTargetType.Entity;
            int? wallSectionIndex = null;
            if (type == ChatMessageType.Order)
            {
                int orderIndex = msg.ReadByte();
                orderTargetCharacter = Entity.FindEntityByID(msg.ReadUInt16()) as Character;
                orderTargetEntity = Entity.FindEntityByID(msg.ReadUInt16()) as Entity;
                int orderOptionIndex = msg.ReadByte();
                orderTargetType = (Order.OrderTargetType)msg.ReadByte();
                if (msg.ReadBoolean())
                {
                    var x = msg.ReadSingle();
                    var y = msg.ReadSingle();
                    var hull = Entity.FindEntityByID(msg.ReadUInt16()) as Hull;
                    orderTargetPosition = new OrderTarget(new Vector2(x, y), hull, true);
                }
                else if (orderTargetType == Order.OrderTargetType.WallSection)
                {
                    wallSectionIndex = msg.ReadByte();
                }

                if (orderIndex < 0 || orderIndex >= Order.PrefabList.Count)
                {
                    DebugConsole.ThrowError($"Invalid order message from client \"{c.Name}\" - order index out of bounds ({orderIndex}, {orderOptionIndex}).");
                    if (NetIdUtils.IdMoreRecent(ID, c.LastSentChatMsgID)) { c.LastSentChatMsgID = ID; }
                    return;
                }

                Order orderPrefab = Order.PrefabList[orderIndex];
                string orderOption = orderOptionIndex < 0 || orderOptionIndex >= orderPrefab.Options.Length ? "" : orderPrefab.Options[orderOptionIndex];
                orderMsg = new OrderChatMessage(orderPrefab, orderOption, orderTargetPosition ?? orderTargetEntity as ISpatialEntity, orderTargetCharacter, c.Character)
                {
                    WallSectionIndex = wallSectionIndex
                };
                txt = orderMsg.Text;
            }
            else
            {
                txt = msg.ReadString() ?? "";
            }

            if (!NetIdUtils.IdMoreRecent(ID, c.LastSentChatMsgID)) { return; }

            c.LastSentChatMsgID = ID;

            if (txt.Length > MaxLength)
            {
                txt = txt.Substring(0, MaxLength);
            }

            c.LastSentChatMessages.Add(txt);
            if (c.LastSentChatMessages.Count > 10)
            {
                c.LastSentChatMessages.RemoveRange(0, c.LastSentChatMessages.Count - 10);
            }

            float similarity = 0.0f;
            for (int i = 0; i < c.LastSentChatMessages.Count; i++)
            {
                float closeFactor = 1.0f / (c.LastSentChatMessages.Count - i);
                    
                if (string.IsNullOrEmpty(txt))
                {
                    similarity += closeFactor;
                }
                else
                {
                    int levenshteinDist = ToolBox.LevenshteinDistance(txt, c.LastSentChatMessages[i]);
                    similarity += Math.Max((txt.Length - levenshteinDist) / (float)txt.Length * closeFactor, 0.0f);
                }
            }
            //order/report messages can be sent a little faster than normal messages without triggering the spam filter
            if (orderMsg != null)
            {
                similarity *= 0.25f;
            }

            bool isOwner = GameMain.Server.OwnerConnection != null && c.Connection == GameMain.Server.OwnerConnection;

            if (similarity + c.ChatSpamSpeed > 5.0f && !isOwner)
            {
                GameMain.Server.KarmaManager.OnSpamFilterTriggered(c);

                c.ChatSpamCount++;
                if (c.ChatSpamCount > 3)
                {
                    //kick for spamming too much
                    GameMain.Server.KickClient(c, TextManager.Get("SpamFilterKicked"));
                }
                else
                {
                    ChatMessage denyMsg = Create("", TextManager.Get("SpamFilterBlocked"), ChatMessageType.Server, null);
                    c.ChatSpamTimer = 10.0f;
                    GameMain.Server.SendDirectChatMessage(denyMsg, c);
                }
                return;
            }

            c.ChatSpamSpeed += similarity + 0.5f;

            if (c.ChatSpamTimer > 0.0f && !isOwner)
            {
                ChatMessage denyMsg = Create("", TextManager.Get("SpamFilterBlocked"), ChatMessageType.Server, null);
                c.ChatSpamTimer = 10.0f;
                GameMain.Server.SendDirectChatMessage(denyMsg, c);
                return;
            }

            var should = GameMain.Lua.hook.Call("chatMessage", new DynValue[] { DynValue.NewString(txt), UserData.Create(c) });


            if(should != null)
			{
				if (should.CastToBool())
				{
                    return;
				}
				else
				{

				}
			}

            if (type == ChatMessageType.Order)
            {
                if (c.Character == null || c.Character.SpeechImpediment >= 100.0f || c.Character.IsDead) { return; }
                Order order = null;
                if (orderMsg.Order.IsReport)
                {
                    HumanAIController.ReportProblem(orderMsg.Sender, orderMsg.Order);
                }
                else if (orderTargetCharacter != null && !orderMsg.Order.TargetAllCharacters)
                {
                    switch (orderTargetType)
                    {
                        case Order.OrderTargetType.Entity:
                            order = new Order(orderMsg.Order.Prefab, orderTargetEntity, orderMsg.Order.Prefab?.GetTargetItemComponent(orderTargetEntity as Item), orderGiver: orderMsg.Sender);
                            break;
                        case Order.OrderTargetType.Position:
                            order = new Order(orderMsg.Order.Prefab, orderTargetPosition, orderGiver: orderMsg.Sender);
                            break;
                    }
                    if (order != null)
                    {
                        orderTargetCharacter.SetOrder(order, orderMsg.OrderOption, orderMsg.Sender);
                    }
                }
                else if (orderMsg.Order.IsIgnoreOrder)
                {
                    switch (orderTargetType)
                    {
                        case Order.OrderTargetType.Entity:
                            if (orderTargetEntity is IIgnorable ignorableEntity)
                            {
                                ignorableEntity.OrderedToBeIgnored = orderMsg.Order.Identifier == "ignorethis";
                            }
                            break;
                        case Order.OrderTargetType.WallSection:
                            if (!wallSectionIndex.HasValue) { break; }
                            if (orderTargetEntity is Structure s && s.GetSection(wallSectionIndex.Value) is IIgnorable ignorableWall)
                            {
                                ignorableWall.OrderedToBeIgnored = orderMsg.Order.Identifier == "ignorethis";
                            }
                            break;
                    }
                }
                GameMain.Server.SendOrderChatMessage(orderMsg);
            }
            else
            {
                GameMain.Server.SendChatMessage(txt, null, c);
            }


        }

        public int EstimateLengthBytesServer(Client c)
        {
            int length = 1 + //(byte)ServerNetObject.CHAT_MESSAGE
                            2 + //(UInt16)NetStateID
                            1 + //(byte)Type
                            Encoding.UTF8.GetBytes(Text).Length + 2;

            if (Sender != null && c.InGame)
            {
                length += 2; //sender ID (UInt16)
            }
            else if (SenderName != null)
            {
                length += Encoding.UTF8.GetBytes(SenderName).Length + 2;
            }

            return length;
        }

        public virtual void ServerWrite(IWriteMessage msg, Client c)
        {
            msg.Write((byte)ServerNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.Write((byte)Type);
            msg.Write((byte)ChangeType);
            msg.Write(Text);

            msg.Write(SenderName);
            msg.Write(Sender != null && c.InGame);
            if (Sender != null && c.InGame)
            {
                msg.Write(Sender.ID);
            }
            if (Type == ChatMessageType.ServerMessageBoxInGame)
            {
                msg.Write(IconStyle);
            }
        }
    }
}
