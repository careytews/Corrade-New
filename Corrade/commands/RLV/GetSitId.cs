///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Globalization;
using OpenMetaverse;
using wasOpenMetaverse;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class RLVBehaviours
        {
            public static readonly Action<string, wasOpenMetaverse.RLV.RLVRule, UUID> getsitid =
                (message, rule, senderUUID) =>
                {
                    int channel;
                    if (!int.TryParse(rule.Param, NumberStyles.Integer, Utils.EnUsCulture, out channel) || channel < 1)
                    {
                        return;
                    }
                    Avatar self;
                    Locks.ClientInstanceNetworkLock.EnterReadLock();
                    var isSitting = Client.Network.CurrentSim.ObjectsAvatars.TryGetValue(Client.Self.LocalID, out self);
                    Locks.ClientInstanceNetworkLock.ExitReadLock();
                    if (isSitting && !self.ParentID.Equals(0))
                    {
                        Primitive sit;
                        Locks.ClientInstanceNetworkLock.EnterReadLock();
                        isSitting = Client.Network.CurrentSim.ObjectsPrimitives.TryGetValue(self.ParentID, out sit);
                        Locks.ClientInstanceNetworkLock.ExitReadLock();
                        if (isSitting)
                        {
                            Locks.ClientInstanceSelfLock.EnterWriteLock();
                            Client.Self.Chat(sit.ID.ToString(), channel, ChatType.Normal);
                            Locks.ClientInstanceSelfLock.ExitWriteLock();
                            return;
                        }
                    }
                    Locks.ClientInstanceSelfLock.EnterWriteLock();
                    Client.Self.Chat(UUID.Zero.ToString(), channel, ChatType.Normal);
                    Locks.ClientInstanceSelfLock.ExitWriteLock();
                };
        }
    }
}
