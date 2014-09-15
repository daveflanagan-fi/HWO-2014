using System.Collections.Generic;

public class Piece
{
    public double length { get; set; }
    public bool? @switch { get; set; }
    public int? radius { get; set; }
    public double? angle { get; set; }

    public int desiredLane { get; set; }
}

public class Lane
{
    public int distanceFromCenter { get; set; }
    public int index { get; set; }
}

public class Position
{
    public double x { get; set; }
    public double y { get; set; }
}

public class StartingPoint
{
    public Position position { get; set; }
    public double angle { get; set; }
}

public class Track
{
    public string id { get; set; }
    public string name { get; set; }
    public List<Piece> pieces { get; set; }
    public List<Lane> lanes { get; set; }
    public StartingPoint startingPoint { get; set; }
}

public class Id
{
    public string name { get; set; }
    public string color { get; set; }
}

public class Dimensions
{
    public double length { get; set; }
    public double width { get; set; }
    public double guideFlagPosition { get; set; }
}

public class Car
{
    public Id id { get; set; }
    public Dimensions dimensions { get; set; }
}

public class RaceSession
{
    public int laps { get; set; }
    public int maxLapTimeMs { get; set; }
    public bool quickRace { get; set; }
}

public class Race
{
    public Track track { get; set; }
    public List<Car> cars { get; set; }
    public RaceSession raceSession { get; set; }
}

public class RaceData
{
    public Race race { get; set; }
}

public class PiecePosition
{
    public int pieceIndex { get; set; }
    public double inPieceDistance { get; set; }
    public Lane lane { get; set; }
    public int lap { get; set; }
}

public class Datum
{
    public Id id { get; set; }
    public double angle { get; set; }
    public PiecePosition piecePosition { get; set; }
}

public class InitObject
{
    public string msgType { get; set; }
    public RaceData data { get; set; }
}

public class PositionObject
{
    public string msgType { get; set; }
    public List<Datum> data { get; set; }
    public string gameId { get; set; }
    public int gameTick { get; set; }
}

public class CarData
{
    public string name { get; set; }
    public string color { get; set; }
}

public class CarObject
{
    public string msgType { get; set; }
    public CarData data { get; set; }
}