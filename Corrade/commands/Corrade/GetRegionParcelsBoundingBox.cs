///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CorradeConfigurationSharp;
using OpenMetaverse;
using wasOpenMetaverse;
using wasSharp;
using Reflection = wasSharp.Reflection;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static readonly Action<Command.CorradeCommandParameters, Dictionary<string, string>>
                getregionparcelsboundingbox =
                    (corradeCommandParameters, result) =>
                    {
                        if (
                            !HasCorradePermission(corradeCommandParameters.Group.UUID,
                                (int) Configuration.Permissions.Land))
                            throw new Command.ScriptException(Enumerations.ScriptError.NO_CORRADE_PERMISSIONS);
                        var region =
                            wasInput(
                                KeyValue.Get(wasOutput(Reflection.GetNameFromEnumValue(Command.ScriptKeys.REGION)),
                                    corradeCommandParameters.Message));
                        Locks.ClientInstanceNetworkLock.EnterReadLock();
                        var simulator = Client.Network.Simulators.AsParallel().FirstOrDefault(
                            o =>
                                o.Name.Equals(
                                    string.IsNullOrEmpty(region) ? Client.Network.CurrentSim.Name : region,
                                    StringComparison.OrdinalIgnoreCase));
                        Locks.ClientInstanceNetworkLock.ExitReadLock();
                        if (simulator == null)
                            throw new Command.ScriptException(Enumerations.ScriptError.REGION_NOT_FOUND);
                        // Get all sim parcels
                        var SimParcelsDownloadedEvent = new ManualResetEventSlim(false);
                        EventHandler<SimParcelsDownloadedEventArgs> SimParcelsDownloadedEventHandler =
                            (sender, args) => SimParcelsDownloadedEvent.Set();
                        Locks.ClientInstanceParcelsLock.EnterReadLock();
                        Client.Parcels.SimParcelsDownloaded += SimParcelsDownloadedEventHandler;
                        Client.Parcels.RequestAllSimParcels(simulator, true, (int) corradeConfiguration.DataTimeout);
                        if (simulator.IsParcelMapFull())
                            SimParcelsDownloadedEvent.Set();
                        if (!SimParcelsDownloadedEvent.Wait((int) corradeConfiguration.ServicesTimeout))
                        {
                            Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                            Locks.ClientInstanceParcelsLock.ExitReadLock();
                            throw new Command.ScriptException(Enumerations.ScriptError.TIMEOUT_GETTING_PARCELS);
                        }
                        Client.Parcels.SimParcelsDownloaded -= SimParcelsDownloadedEventHandler;
                        Locks.ClientInstanceParcelsLock.ExitReadLock();
                        var csv = new List<Vector3>();
                        simulator.Parcels.ForEach(o => csv.AddRange(new[] {o.AABBMin, o.AABBMax}));
                        if (csv.Any())
                            result.Add(Reflection.GetNameFromEnumValue(Command.ResultKeys.DATA),
                                CSV.FromEnumerable(csv.Select(o => o.ToString())));
                    };
        }
    }
}