///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using OpenMetaverse;
using Parallel = System.Threading.Tasks.Parallel;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static Action<Group, string, Dictionary<string, string>> directorysearch =
                (commandGroup, message, result) =>
                {
                    if (!HasCorradePermission(commandGroup.Name, (int) Permissions.Directory))
                    {
                        throw new ScriptException(ScriptError.NO_CORRADE_PERMISSIONS);
                    }
                    wasAdaptiveAlarm DirectorySearchResultsAlarm =
                        new wasAdaptiveAlarm(corradeConfiguration.DataDecayType);
                    string name =
                        wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.NAME)),
                            message));
                    string fields =
                        wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                            message));
                    object LockObject = new object();
                    List<string> csv = new List<string>();
                    int handledEvents = 0;
                    int counter = 1;
                    switch (
                        wasGetEnumValueFromDescription<Type>(
                            wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TYPE)), message))
                                .ToLowerInvariant()))
                    {
                        case Type.CLASSIFIED:
                            DirectoryManager.Classified searchClassified = new DirectoryManager.Classified();
                            wasCSVToStructure(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                        message)),
                                ref searchClassified);
                            Dictionary<DirectoryManager.Classified, int> classifieds =
                                new Dictionary<DirectoryManager.Classified, int>();
                            EventHandler<DirClassifiedsReplyEventArgs> DirClassifiedsEventHandler =
                                (sender, args) => Parallel.ForEach(args.Classifieds, o =>
                                {
                                    DirectorySearchResultsAlarm.Alarm(corradeConfiguration.DataTimeout);
                                    int score = !string.IsNullOrEmpty(fields)
                                        ? wasGetFields(searchClassified, searchClassified.GetType().Name)
                                            .Sum(
                                                p =>
                                                    (from q in
                                                        wasGetFields(o,
                                                            o.GetType().Name)
                                                        let r = wasGetInfoValue(p.Key, p.Value)
                                                        where r != null
                                                        let s = wasGetInfoValue(q.Key, q.Value)
                                                        where s != null
                                                        where r.Equals(s)
                                                        select r).Count())
                                        : 0;
                                    lock (LockObject)
                                    {
                                        if (!classifieds.ContainsKey(o))
                                        {
                                            classifieds.Add(o, score);
                                        }
                                    }
                                });
                            lock (ClientInstanceDirectoryLock)
                            {
                                Client.Directory.DirClassifiedsReply += DirClassifiedsEventHandler;
                                Client.Directory.StartClassifiedSearch(name);
                                DirectorySearchResultsAlarm.Signal.WaitOne(
                                    (int) corradeConfiguration.ServicesTimeout,
                                    false);
                                Client.Directory.DirClassifiedsReply -= DirClassifiedsEventHandler;
                            }
                            Dictionary<DirectoryManager.Classified, int> safeClassifieds;
                            lock (LockObject)
                            {
                                safeClassifieds =
                                    classifieds.OrderByDescending(o => o.Value)
                                        .ToDictionary(o => o.Key, p => p.Value);
                            }
                            Parallel.ForEach(safeClassifieds,
                                o => Parallel.ForEach(wasGetFields(o.Key, o.Key.GetType().Name), p =>
                                {
                                    lock (LockObject)
                                    {
                                        csv.Add(p.Key.Name);
                                        csv.AddRange(wasGetInfo(p.Key, p.Value));
                                    }
                                }));
                            break;
                        case Type.EVENT:
                            DirectoryManager.EventsSearchData searchEvent = new DirectoryManager.EventsSearchData();
                            wasCSVToStructure(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                        message)),
                                ref searchEvent);
                            Dictionary<DirectoryManager.EventsSearchData, int> events =
                                new Dictionary<DirectoryManager.EventsSearchData, int>();
                            EventHandler<DirEventsReplyEventArgs> DirEventsEventHandler =
                                (sender, args) =>
                                {
                                    DirectorySearchResultsAlarm.Alarm(corradeConfiguration.DataTimeout);
                                    handledEvents += args.MatchedEvents.Count;
                                    Parallel.ForEach(args.MatchedEvents, o =>
                                    {
                                        int score = !string.IsNullOrEmpty(fields)
                                            ? wasGetFields(searchEvent, searchEvent.GetType().Name)
                                                .Sum(
                                                    p =>
                                                        (from q in
                                                            wasGetFields(o, o.GetType().Name)
                                                            let r = wasGetInfoValue(p.Key, p.Value)
                                                            where r != null
                                                            let s = wasGetInfoValue(q.Key, q.Value)
                                                            where s != null
                                                            where r.Equals(s)
                                                            select r).Count())
                                            : 0;
                                        lock (LockObject)
                                        {
                                            if (!events.ContainsKey(o))
                                            {
                                                events.Add(o, score);
                                            }
                                        }
                                    });
                                    if (handledEvents > LINDEN_CONSTANTS.DIRECTORY.EVENT.SEARCH_RESULTS_COUNT &&
                                        ((handledEvents - counter)%
                                         LINDEN_CONSTANTS.DIRECTORY.EVENT.SEARCH_RESULTS_COUNT).Equals(0))
                                    {
                                        ++counter;
                                        Client.Directory.StartEventsSearch(name, (uint) handledEvents);
                                    }
                                };
                            lock (ClientInstanceDirectoryLock)
                            {
                                Client.Directory.DirEventsReply += DirEventsEventHandler;
                                Client.Directory.StartEventsSearch(name,
                                    (uint) handledEvents);
                                DirectorySearchResultsAlarm.Signal.WaitOne(
                                    (int) corradeConfiguration.ServicesTimeout,
                                    false);
                                Client.Directory.DirEventsReply -= DirEventsEventHandler;
                            }
                            Dictionary<DirectoryManager.EventsSearchData, int> safeEvents;
                            lock (LockObject)
                            {
                                safeEvents = events.OrderByDescending(o => o.Value)
                                    .ToDictionary(o => o.Key, p => p.Value);
                            }
                            Parallel.ForEach(safeEvents,
                                o => Parallel.ForEach(wasGetFields(o.Key, o.Key.GetType().Name), p =>
                                {
                                    lock (LockObject)
                                    {
                                        csv.Add(p.Key.Name);
                                        csv.AddRange(wasGetInfo(p.Key, p.Value));
                                    }
                                }));
                            break;
                        case Type.GROUP:
                            if (string.IsNullOrEmpty(name))
                            {
                                throw new ScriptException(ScriptError.NO_SEARCH_TEXT_PROVIDED);
                            }
                            DirectoryManager.GroupSearchData searchGroup = new DirectoryManager.GroupSearchData();
                            wasCSVToStructure(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                        message)),
                                ref searchGroup);
                            Dictionary<DirectoryManager.GroupSearchData, int> groups =
                                new Dictionary<DirectoryManager.GroupSearchData, int>();
                            EventHandler<DirGroupsReplyEventArgs> DirGroupsEventHandler =
                                (sender, args) =>
                                {
                                    DirectorySearchResultsAlarm.Alarm(corradeConfiguration.DataTimeout);
                                    handledEvents += args.MatchedGroups.Count;
                                    Parallel.ForEach(args.MatchedGroups, o =>
                                    {
                                        int score = !string.IsNullOrEmpty(fields)
                                            ? wasGetFields(searchGroup, searchGroup.GetType().Name)
                                                .Sum(
                                                    p =>
                                                        (from q in
                                                            wasGetFields(o, o.GetType().Name)
                                                            let r = wasGetInfoValue(p.Key, p.Value)
                                                            where r != null
                                                            let s = wasGetInfoValue(q.Key, q.Value)
                                                            where s != null
                                                            where r.Equals(s)
                                                            select r).Count())
                                            : 0;
                                        lock (LockObject)
                                        {
                                            if (!groups.ContainsKey(o))
                                            {
                                                groups.Add(o, score);
                                            }
                                        }
                                    });
                                    if (handledEvents > LINDEN_CONSTANTS.DIRECTORY.GROUP.SEARCH_RESULTS_COUNT &&
                                        ((handledEvents - counter)%
                                         LINDEN_CONSTANTS.DIRECTORY.GROUP.SEARCH_RESULTS_COUNT).Equals(0))
                                    {
                                        ++counter;
                                        Client.Directory.StartGroupSearch(name, handledEvents);
                                    }
                                };
                            lock (ClientInstanceDirectoryLock)
                            {
                                Client.Directory.DirGroupsReply += DirGroupsEventHandler;
                                Client.Directory.StartGroupSearch(name, handledEvents);
                                DirectorySearchResultsAlarm.Signal.WaitOne(
                                    (int) corradeConfiguration.ServicesTimeout,
                                    false);
                                Client.Directory.DirGroupsReply -= DirGroupsEventHandler;
                            }
                            Dictionary<DirectoryManager.GroupSearchData, int> safeGroups;
                            lock (LockObject)
                            {
                                safeGroups = groups.OrderByDescending(o => o.Value)
                                    .ToDictionary(o => o.Key, p => p.Value);
                            }
                            Parallel.ForEach(safeGroups,
                                o => Parallel.ForEach(wasGetFields(o.Key, o.Key.GetType().Name), p =>
                                {
                                    lock (LockObject)
                                    {
                                        csv.Add(p.Key.Name);
                                        csv.AddRange(wasGetInfo(p.Key, p.Value));
                                    }
                                }));
                            break;
                        case Type.LAND:
                            DirectoryManager.DirectoryParcel searchLand = new DirectoryManager.DirectoryParcel();
                            wasCSVToStructure(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                        message)),
                                ref searchLand);
                            Dictionary<DirectoryManager.DirectoryParcel, int> lands =
                                new Dictionary<DirectoryManager.DirectoryParcel, int>();
                            EventHandler<DirLandReplyEventArgs> DirLandReplyEventArgs =
                                (sender, args) =>
                                {
                                    DirectorySearchResultsAlarm.Alarm(corradeConfiguration.DataTimeout);
                                    handledEvents += args.DirParcels.Count;
                                    Parallel.ForEach(args.DirParcels, o =>
                                    {
                                        int score = !string.IsNullOrEmpty(fields)
                                            ? wasGetFields(searchLand, searchLand.GetType().Name)
                                                .Sum(
                                                    p =>
                                                        (from q in
                                                            wasGetFields(o, o.GetType().Name)
                                                            let r = wasGetInfoValue(p.Key, p.Value)
                                                            where r != null
                                                            let s = wasGetInfoValue(q.Key, q.Value)
                                                            where s != null
                                                            where r.Equals(s)
                                                            select r).Count())
                                            : 0;
                                        lock (LockObject)
                                        {
                                            if (!lands.ContainsKey(o))
                                            {
                                                lands.Add(o, score);
                                            }
                                        }
                                    });
                                    if (handledEvents > LINDEN_CONSTANTS.DIRECTORY.LAND.SEARCH_RESULTS_COUNT &&
                                        ((handledEvents - counter)%
                                         LINDEN_CONSTANTS.DIRECTORY.LAND.SEARCH_RESULTS_COUNT).Equals(0))
                                    {
                                        ++counter;
                                        Client.Directory.StartLandSearch(DirectoryManager.DirFindFlags.SortAsc,
                                            DirectoryManager.SearchTypeFlags.Any, int.MaxValue, int.MaxValue,
                                            handledEvents);
                                    }
                                };
                            lock (ClientInstanceDirectoryLock)
                            {
                                Client.Directory.DirLandReply += DirLandReplyEventArgs;
                                Client.Directory.StartLandSearch(DirectoryManager.DirFindFlags.SortAsc,
                                    DirectoryManager.SearchTypeFlags.Any, int.MaxValue, int.MaxValue, handledEvents);
                                DirectorySearchResultsAlarm.Signal.WaitOne(
                                    (int) corradeConfiguration.ServicesTimeout,
                                    false);
                                Client.Directory.DirLandReply -= DirLandReplyEventArgs;
                            }
                            Dictionary<DirectoryManager.DirectoryParcel, int> safeLands;
                            lock (LockObject)
                            {
                                safeLands = lands.OrderByDescending(o => o.Value)
                                    .ToDictionary(o => o.Key, p => p.Value);
                            }
                            Parallel.ForEach(safeLands,
                                o => Parallel.ForEach(wasGetFields(o.Key, o.Key.GetType().Name), p =>
                                {
                                    lock (LockObject)
                                    {
                                        csv.Add(p.Key.Name);
                                        csv.AddRange(wasGetInfo(p.Key, p.Value));
                                    }
                                }));
                            break;
                        case Type.PEOPLE:
                            if (string.IsNullOrEmpty(name))
                            {
                                throw new ScriptException(ScriptError.NO_SEARCH_TEXT_PROVIDED);
                            }
                            DirectoryManager.AgentSearchData searchAgent = new DirectoryManager.AgentSearchData();
                            Dictionary<DirectoryManager.AgentSearchData, int> agents =
                                new Dictionary<DirectoryManager.AgentSearchData, int>();
                            EventHandler<DirPeopleReplyEventArgs> DirPeopleReplyEventHandler =
                                (sender, args) =>
                                {
                                    DirectorySearchResultsAlarm.Alarm(corradeConfiguration.DataTimeout);
                                    handledEvents += args.MatchedPeople.Count;
                                    Parallel.ForEach(args.MatchedPeople, o =>
                                    {
                                        int score = !string.IsNullOrEmpty(fields)
                                            ? wasGetFields(searchAgent, searchAgent.GetType().Name)
                                                .Sum(
                                                    p =>
                                                        (from q in
                                                            wasGetFields(o, o.GetType().Name)
                                                            let r = wasGetInfoValue(p.Key, p.Value)
                                                            where r != null
                                                            let s = wasGetInfoValue(q.Key, q.Value)
                                                            where s != null
                                                            where r.Equals(s)
                                                            select r).Count())
                                            : 0;
                                        lock (LockObject)
                                        {
                                            if (!agents.ContainsKey(o))
                                            {
                                                agents.Add(o, score);
                                            }
                                        }
                                    });
                                    if (handledEvents > LINDEN_CONSTANTS.DIRECTORY.PEOPLE.SEARCH_RESULTS_COUNT &&
                                        ((handledEvents - counter)%
                                         LINDEN_CONSTANTS.DIRECTORY.PEOPLE.SEARCH_RESULTS_COUNT).Equals(0))
                                    {
                                        ++counter;
                                        Client.Directory.StartPeopleSearch(name, handledEvents);
                                    }
                                };
                            lock (ClientInstanceDirectoryLock)
                            {
                                Client.Directory.DirPeopleReply += DirPeopleReplyEventHandler;
                                Client.Directory.StartPeopleSearch(name, handledEvents);
                                DirectorySearchResultsAlarm.Signal.WaitOne(
                                    (int) corradeConfiguration.ServicesTimeout,
                                    false);
                                Client.Directory.DirPeopleReply -= DirPeopleReplyEventHandler;
                            }
                            Dictionary<DirectoryManager.AgentSearchData, int> safeAgents;
                            lock (LockObject)
                            {
                                safeAgents = agents.OrderByDescending(o => o.Value)
                                    .ToDictionary(o => o.Key, p => p.Value);
                            }
                            Parallel.ForEach(safeAgents,
                                o => Parallel.ForEach(wasGetFields(o.Key, o.Key.GetType().Name), p =>
                                {
                                    lock (LockObject)
                                    {
                                        csv.Add(p.Key.Name);
                                        csv.AddRange(wasGetInfo(p.Key, p.Value));
                                    }
                                }));
                            break;
                        case Type.PLACE:
                            if (string.IsNullOrEmpty(name))
                            {
                                throw new ScriptException(ScriptError.NO_SEARCH_TEXT_PROVIDED);
                            }
                            DirectoryManager.PlacesSearchData searchPlaces = new DirectoryManager.PlacesSearchData();
                            wasCSVToStructure(
                                wasInput(
                                    wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DATA)),
                                        message)),
                                ref searchPlaces);
                            Dictionary<DirectoryManager.PlacesSearchData, int> places =
                                new Dictionary<DirectoryManager.PlacesSearchData, int>();
                            EventHandler<PlacesReplyEventArgs> DirPlacesReplyEventHandler =
                                (sender, args) => Parallel.ForEach(args.MatchedPlaces, o =>
                                {
                                    DirectorySearchResultsAlarm.Alarm(corradeConfiguration.DataTimeout);
                                    int score = !string.IsNullOrEmpty(fields)
                                        ? wasGetFields(searchPlaces, searchPlaces.GetType().Name)
                                            .Sum(
                                                p =>
                                                    (from q in
                                                        wasGetFields(o, o.GetType().Name)
                                                        let r = wasGetInfoValue(p.Key, p.Value)
                                                        where r != null
                                                        let s = wasGetInfoValue(q.Key, q.Value)
                                                        where s != null
                                                        where r.Equals(s)
                                                        select r).Count())
                                        : 0;
                                    lock (LockObject)
                                    {
                                        if (!places.ContainsKey(o))
                                        {
                                            places.Add(o, score);
                                        }
                                    }
                                });
                            lock (ClientInstanceDirectoryLock)
                            {
                                Client.Directory.PlacesReply += DirPlacesReplyEventHandler;
                                Client.Directory.StartPlacesSearch(name);
                                DirectorySearchResultsAlarm.Signal.WaitOne(
                                    (int) corradeConfiguration.ServicesTimeout,
                                    false);
                                Client.Directory.PlacesReply -= DirPlacesReplyEventHandler;
                            }
                            Dictionary<DirectoryManager.PlacesSearchData, int> safePlaces;
                            lock (LockObject)
                            {
                                safePlaces = places.OrderByDescending(o => o.Value)
                                    .ToDictionary(o => o.Key, p => p.Value);
                            }
                            Parallel.ForEach(safePlaces,
                                o => Parallel.ForEach(wasGetFields(o.Key, o.Key.GetType().Name), p =>
                                {
                                    lock (LockObject)
                                    {
                                        csv.Add(p.Key.Name);
                                        csv.AddRange(wasGetInfo(p.Key, p.Value));
                                    }
                                }));
                            break;
                        default:
                            throw new ScriptException(ScriptError.UNKNOWN_DIRECTORY_SEARCH_TYPE);
                    }
                    if (csv.Any())
                    {
                        result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                            wasEnumerableToCSV(csv));
                    }
                };
        }
    }
}