using System;
using System.Collections.Generic;

namespace Naziki_Editor.Core.Abstractions
{
    public interface ITrackBlueprintManager
    {
        List<TrackBlueprint> ControllerBlueprints { get; }
        List<TrackBlueprint> GetBlueprintsForType(Type type);
    }
}