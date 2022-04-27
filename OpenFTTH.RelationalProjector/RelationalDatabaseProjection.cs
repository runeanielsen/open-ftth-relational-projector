﻿using Microsoft.Extensions.Logging;
using OpenFTTH.EventSourcing;
using OpenFTTH.RelationalProjector.Database;
using OpenFTTH.RelationalProjector.State;
using OpenFTTH.RouteNetwork.API.Model;
using OpenFTTH.RouteNetwork.Business.Interest.Events;
using OpenFTTH.UtilityGraphService.Business.NodeContainers.Events;
using OpenFTTH.UtilityGraphService.Business.SpanEquipments.Events;
using OpenFTTH.UtilityGraphService.Business.TerminalEquipments.Events;
using System;
using System.Collections.Generic;

namespace OpenFTTH.RelationalProjector
{
    public class RelationalDatabaseProjection : ProjectionBase
    {
        private readonly string _schemaName = "utility_network";

        private readonly ILogger<RelationalDatabaseProjection> _logger;
        private readonly PostgresWriter _dbWriter;

        private readonly ProjektorState _state = new();

        private bool _bulkMode = true;

        public RelationalDatabaseProjection(ILogger<RelationalDatabaseProjection> logger, PostgresWriter dbWriter)
        {
            _logger = logger;
            _dbWriter = dbWriter;

            // Node container events
            ProjectEvent<NodeContainerPlacedInRouteNetwork>(Project);
            ProjectEvent<NodeContainerRemovedFromRouteNetwork>(Project);

            // Interest events
            ProjectEvent<WalkOfInterestRegistered>(Project);
            ProjectEvent<WalkOfInterestRouteNetworkElementsModified>(Project);
            ProjectEvent<InterestUnregistered>(Project);

            // Span equipment events
            ProjectEvent<SpanEquipmentPlacedInRouteNetwork>(Project);
            ProjectEvent<SpanEquipmentMoved>(Project);
            ProjectEvent<SpanEquipmentRemoved>(Project);
            ProjectEvent<SpanSegmentsConnectedToSimpleTerminals>(Project);
            ProjectEvent<SpanSegmentsDisconnectedFromTerminals>(Project);
            ProjectEvent<SpanEquipmentAffixedToParent>(Project);
            ProjectEvent<SpanEquipmentDetachedFromParent>(Project);

            // Span equipment specification events
            ProjectEvent<SpanEquipmentSpecificationAdded>(Project);
            ProjectEvent<SpanStructureSpecificationAdded>(Project);
            ProjectEvent<SpanEquipmentSpecificationChanged>(Project);

            // Terminal equipment events
            ProjectEvent<TerminalEquipmentSpecificationAdded>(Project);
            ProjectEvent<TerminalEquipmentPlacedInNodeContainer>(Project);
            ProjectEvent<TerminalEquipmentRemoved>(Project);
            ProjectEvent<TerminalEquipmentNamingInfoChanged>(Project);
        }

        private void PrepareDatabase()
        {
            _dbWriter.CreateSchema(_schemaName);
            _dbWriter.CreateRouteElementToInterestTable(_schemaName);
            _dbWriter.CreateConduitTable(_schemaName);
            _dbWriter.CreateRouteSegmentLabelView(_schemaName);
            _dbWriter.CreateServiceTerminationTable(_schemaName);
            _dbWriter.CreateConduitSlackTable(_schemaName);
            _dbWriter.CreateRouteNodeView(_schemaName);
            _dbWriter.CreateRouteSegmentView(_schemaName);
        }

        private void Project(IEventEnvelope eventEnvelope)
        {
            switch (eventEnvelope.Data)
            {
                // Node container events
                case (NodeContainerPlacedInRouteNetwork @event):
                    _state.ProcessNodeContainerAdded(@event);
                    break;

                case (NodeContainerRemovedFromRouteNetwork @event):
                    _state.ProcessNodeContainerRemoved(@event.NodeContainerId);
                    break;


                // Route network interest events
                case (WalkOfInterestRegistered @event):
                    Handle(@event);
                    break;

                case (WalkOfInterestRouteNetworkElementsModified @event):
                    Handle(@event);
                    break;

                case (InterestUnregistered @event):
                    Handle(@event);
                    break;


                // Span equipment events
                case (SpanEquipmentPlacedInRouteNetwork @event):
                    Handle(@event);
                    break;

                case (SpanEquipmentMoved @event):
                    _state.ProcessSpanEquipmentMoved(@event);
                    break;

                case (SpanEquipmentRemoved @event):
                    Handle(@event);
                    break;

                case (SpanSegmentsConnectedToSimpleTerminals @event):
                    _state.ProcessSpanEquipmentConnects(@event);
                    break;

                case (SpanSegmentsDisconnectedFromTerminals @event):
                    _state.ProcessSpanEquipmentDisconnects(@event);
                    break;

                case (SpanEquipmentAffixedToParent @event):
                    _state.ProcessSpanEquipmentAffixedToParent(@event);
                    break;

                case (SpanEquipmentDetachedFromParent @event):
                    _state.ProcessSpanEquipmentDetachedFromParent(@event);
                    break;


                // Span equipment specification events
                case (SpanEquipmentSpecificationAdded @event):
                    _state.ProcessSpanEquipmentSpecificationAdded(@event);
                    break;

                case (SpanStructureSpecificationAdded @event):
                    _state.ProcessSpanStructureSpecificationAdded(@event);
                    break;

                case (SpanEquipmentSpecificationChanged @event):
                    Handle(@event);
                    break;


                // Terminal equipment events
                case (TerminalEquipmentSpecificationAdded @event):
                    _state.ProcessTerminalEquipmentSpecificationAdded(@event);
                    break;

                case (TerminalEquipmentPlacedInNodeContainer @event):
                    Handle(@event);
                    break;

                /*
                case (TerminalEquipmentRemoved @event):
                    Handle(@event);
                    break;
                */
            }
        }

        #region Interest events

        private void Handle(WalkOfInterestRegistered @event)
        {
            if (_bulkMode)
            {
                _state.ProcessWalkOfInterestAdded(@event.Interest);
            }
            else
            {
                _dbWriter.InsertGuidsIntoRouteElementToInterestTable(_schemaName, @event.Interest.Id, RemoveDublicatedIds(@event.Interest.RouteNetworkElementRefs));
            }
        }

        private void Handle(WalkOfInterestRouteNetworkElementsModified @event)
        {
            if (_bulkMode)
            {
                _state.ProcessWalkOfInterestUpdated(@event.InterestId, @event.RouteNetworkElementIds);
            }
            else
            {
                _dbWriter.DeleteGuidsFromRouteElementToInterestTable(_schemaName, @event.InterestId);
                _dbWriter.InsertGuidsIntoRouteElementToInterestTable(_schemaName, @event.InterestId, RemoveDublicatedIds(@event.RouteNetworkElementIds));
            }
        }

        private void Handle(InterestUnregistered @event)
        {
            if (_bulkMode)
            {
                _state.ProcessInterestRemoved(@event.InterestId);
            }
            else
            {
                _dbWriter.DeleteGuidsFromRouteElementToInterestTable(_schemaName, @event.InterestId);
            }
        }

        #endregion

        #region Span Equipment Events
        private void Handle(SpanEquipmentPlacedInRouteNetwork @event)
        {
            _state.ProcessSpanEquipmentAdded(@event.Equipment);

            if (!_bulkMode)
            {
                var spanEquipmentSpec = _state.GetSpanEquipmentSpecification(@event.Equipment.SpecificationId);
                var structureSpec = _state.GetSpanStructureSpecification(spanEquipmentSpec.RootTemplate.SpanStructureSpecificationId);

                if (!@event.Equipment.IsCable)
                    _dbWriter.InsertSpanEquipmentIntoConduitTable(_schemaName, @event.Equipment.Id, @event.Equipment.WalkOfInterestId, structureSpec.OuterDiameter.Value);
            }
        }

        private void Handle(SpanEquipmentRemoved @event)
        {
            if (_state.TryGetSpanEquipmentState(@event.SpanEquipmentId, out var spanEquipmentState))
            {
                if (!_bulkMode)
                {
                    if (!spanEquipmentState.IsCable)
                        _dbWriter.DeleteSpanEquipmentFromConduitTable(_schemaName, @event.SpanEquipmentId);
                }

                _state.ProcessSpanEquipmentRemoved(@event.SpanEquipmentId);
            }
        }

        #endregion

        #region Span Equipment Specification Events

        private void Handle(SpanEquipmentSpecificationChanged @event)
        {
            _state.ProcessSpanEquipmentChanged(@event);

            if (!_bulkMode)
            {
                var outerDiameter = _state.GetSpanStructureSpecification(_state.GetSpanEquipmentSpecification(@event.NewSpecificationId).RootTemplate.SpanStructureSpecificationId).OuterDiameter;

                _dbWriter.UpdateSpanEquipmentDiameterInConduitTable(_schemaName, @event.SpanEquipmentId, outerDiameter.Value);
            }
        }


        #endregion

        #region Terminal equipment events
        
        private void Handle(TerminalEquipmentPlacedInNodeContainer @event)
        {
            var serviceTerminationState = _state.ProcessServiceTerminationstAdded(@event);

            if (serviceTerminationState != null && !_bulkMode)
            {
               _dbWriter.InsertIntoServiceTerminationTable(_schemaName, serviceTerminationState);
            }
        }

        #endregion

        public override void DehydrationFinish()
        {
            PrepareDatabase();

            _logger.LogInformation($"Bulk write to tables in schema: '{_schemaName}' started...");

            _logger.LogInformation($"Writing route element interest relations...");
            _dbWriter.BulkCopyGuidsToRouteElementToInterestTable(_schemaName, _state);

            _logger.LogInformation($"Writing service terminations...");
            _dbWriter.BulkCopyIntoServiceTerminationTable(_schemaName, _state);

            _logger.LogInformation($"Writing conduits...");
            _dbWriter.BulkCopyIntoConduitTable(_schemaName, _state);

            _logger.LogInformation($"Writing conduit slacks...");
            _dbWriter.BulkCopyIntoConduitSlackTable(_schemaName, _state);

            _bulkMode = false;

            _logger.LogInformation("Bulk write finish.");
        }

        private IEnumerable<Guid> RemoveDublicatedIds(RouteNetworkElementIdList routeNetworkElementRefs)
        {
            RouteNetworkElementIdList result = new();

            HashSet<Guid> alreadyAdded = new();

            foreach (var id in routeNetworkElementRefs)
            {
                if (!alreadyAdded.Contains(id))
                {
                    alreadyAdded.Add(id);
                    result.Add(id);
                }
            }

            return result;
        }

    }
}
