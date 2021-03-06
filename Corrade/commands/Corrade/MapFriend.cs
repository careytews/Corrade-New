///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Threading;
using CorradeConfigurationSharp;
using OpenMetaverse;
using wasOpenMetaverse;
using wasSharp;
using wasSharp.Timers;
using Reflection = wasSharp.Reflection;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static readonly Action<Command.CorradeCommandParameters, Dictionary<string, string>> mapfriend =
                (corradeCommandParameters, result) =>
                {
                    if (
                        !HasCorradePermission(corradeCommandParameters.Group.UUID,
                            (int) Configuration.Permissions.Friendship))
                        throw new Command.ScriptException(Enumerations.ScriptError.NO_CORRADE_PERMISSIONS);
                    UUID agentUUID;
                    if (
                        !UUID.TryParse(
                            wasInput(KeyValue.Get(
                                wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.AGENT)),
                                corradeCommandParameters.Message)),
                            out agentUUID) && !Resolvers.AgentNameToUUID(Client,
                            wasInput(
                                KeyValue.Get(
                                    wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.FIRSTNAME)),
                                    corradeCommandParameters.Message)),
                            wasInput(
                                KeyValue.Get(
                                    wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.LASTNAME)),
                                    corradeCommandParameters.Message)),
                            corradeConfiguration.ServicesTimeout, corradeConfiguration.DataTimeout,
                            new DecayingAlarm(corradeConfiguration.DataDecayType),
                            ref agentUUID))
                        throw new Command.ScriptException(Enumerations.ScriptError.AGENT_NOT_FOUND);

                    FriendInfo friend;
                    Locks.ClientInstanceFriendsLock.EnterReadLock();
                    if (!Client.Friends.FriendList.TryGetValue(agentUUID, out friend))
                    {
                        Locks.ClientInstanceFriendsLock.ExitReadLock();
                        throw new Command.ScriptException(Enumerations.ScriptError.FRIEND_NOT_FOUND);
                    }
                    Locks.ClientInstanceFriendsLock.ExitReadLock();

                    if (!friend.CanSeeThemOnMap)
                        throw new Command.ScriptException(Enumerations.ScriptError.FRIEND_DOES_NOT_ALLOW_MAPPING);
                    ulong regionHandle = 0;
                    var position = Vector3.Zero;
                    var FriendFoundEvent = new ManualResetEventSlim(false);
                    var offline = false;
                    EventHandler<FriendFoundReplyEventArgs> FriendFoundEventHandler = (sender, args) =>
                    {
                        if (!args.AgentID.Equals(agentUUID))
                            return;
                        if (args.RegionHandle.Equals(0))
                        {
                            offline = true;
                            FriendFoundEvent.Set();
                            return;
                        }
                        regionHandle = args.RegionHandle;
                        position = args.Location;
                        FriendFoundEvent.Set();
                    };
                    Locks.ClientInstanceFriendsLock.EnterReadLock();
                    Client.Friends.FriendFoundReply += FriendFoundEventHandler;
                    Client.Friends.MapFriend(agentUUID);
                    if (!FriendFoundEvent.Wait((int) corradeConfiguration.ServicesTimeout))
                    {
                        Client.Friends.FriendFoundReply -= FriendFoundEventHandler;
                        Locks.ClientInstanceFriendsLock.ExitReadLock();
                        throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_MAPPING_FRIEND);
                    }
                    Client.Friends.FriendFoundReply -= FriendFoundEventHandler;
                    Locks.ClientInstanceFriendsLock.ExitReadLock();
                    if (offline)
                        throw new Command.ScriptException(Enumerations.ScriptError.FRIEND_OFFLINE);
                    var parcelUUID = Client.Parcels.RequestRemoteParcelID(position, regionHandle, UUID.Zero);
                    var ParcelInfoEvent = new ManualResetEventSlim(false);
                    var regionName = string.Empty;
                    EventHandler<ParcelInfoReplyEventArgs> ParcelInfoEventHandler = (sender, args) =>
                    {
                        if (!args.Parcel.ID.Equals(parcelUUID))
                            return;

                        regionName = args.Parcel.SimName;
                        ParcelInfoEvent.Set();
                    };
                    Locks.ClientInstanceParcelsLock.EnterReadLock();
                    Client.Parcels.ParcelInfoReply += ParcelInfoEventHandler;
                    Client.Parcels.RequestParcelInfo(parcelUUID);
                    if (!ParcelInfoEvent.Wait((int) corradeConfiguration.ServicesTimeout))
                    {
                        Client.Parcels.ParcelInfoReply -= ParcelInfoEventHandler;
                        Locks.ClientInstanceParcelsLock.ExitReadLock();
                        throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_GETTING_PARCELS);
                    }
                    Client.Parcels.ParcelInfoReply -= ParcelInfoEventHandler;
                    Locks.ClientInstanceParcelsLock.ExitReadLock();
                    result.Add(Reflection.GetNameFromEnumValue(Command.ResultKeys.DATA),
                        CSV.FromEnumerable(new[] {regionName, position.ToString()}));
                };
        }
    }
}