using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;

public class Bot
{
    public Race Race { get; set; }
    public CarObject Car { get; set; }

    public int CurrentLane { get; set; }
    public double CurrentThrottle { get; set; }
    public double Speed { get; set; }
    public double TrackLength { get; set; }
    public double Distance { get; set; }

    private int _lastLane;
    private double _lastThrottle;
    private int _lastIndex;
    private double _lastSpeed;
    private double _lastPos;
    private double _lastAngle;

    private Queue<SendMsg> _lowPriority = new Queue<SendMsg>();
    private Queue<SendMsg> _highPriority = new Queue<SendMsg>();

    public static void Main(string[] args)
    {
        string host = args[0];
        int port = int.Parse(args[1]);
        string botName = args[2];
        string botKey = args[3];

        Console.WriteLine("Connecting to " + host + ":" + port + " as " + botName + "/" + botKey);

        using (TcpClient client = new TcpClient(host, port))
        {
            NetworkStream stream = client.GetStream();
            StreamReader reader = new StreamReader(stream);
            StreamWriter writer = new StreamWriter(stream);
            writer.AutoFlush = true;

            new Bot(reader, writer, new Join(botName, botKey));
        }
    }

    private StreamWriter writer;

    Bot(StreamReader reader, StreamWriter writer, SendMsg join)
    {
        this.writer = writer;
        string line;

        CurrentThrottle = 1;
        CurrentLane = -1;

        send(join);

        while ((line = reader.ReadLine()) != null)
        {
            MsgWrapper msg = JsonConvert.DeserializeObject<MsgWrapper>(line);
            switch (msg.msgType)
            {
                case "carPositions":
                    PositionObject pos = JsonConvert.DeserializeObject<PositionObject>(line);
                    foreach (Datum testCar in pos.data)
                    {
                        if (testCar.id.name == Car.data.name)
                        {
                            Process(testCar);
                            break;
                        }
                    }
                    break;
                case "yourCar":
                    Car = JsonConvert.DeserializeObject<CarObject>(line);
                    break;
                case "gameInit":
                    InitObject init = JsonConvert.DeserializeObject<InitObject>(line);
                    Race = init.data.race;
                    CalculateFastestRoute();
                    TrackLength = CalculateRouteLength();
                    break;
                case "gameEnd":
                    Console.WriteLine("Race Over!");
                    break;
                case "gameStart":
                    Console.WriteLine("Race Start!");
                    break;
                case "crash":
                    CrashObject crash = JsonConvert.DeserializeObject<CrashObject>(line);
                    Console.WriteLine("Crash {0}", crash.data.name);
                    break;
                case "spawn":
                    SpawnObject spawn = JsonConvert.DeserializeObject<SpawnObject>(line);
                    Console.WriteLine("Spawn {0}", spawn.data.name);
                    break;
                case "lapFinished":
                    LapObject lap = JsonConvert.DeserializeObject<LapObject>(line);
                    Console.WriteLine("Lap Complete {0} {1}", lap.data.car.name, (double)lap.data.lapTime.millis / 1000);
                    break;
                case "joinRace":
                    break;
                default:
                    //Console.WriteLine(line);
                    break;
            }

            Tick();
        }
    }

    static string UppercaseFirst(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        return char.ToUpper(s[0]) + s.Substring(1);
    }

    private void Process(Datum data)
    {
        int next = data.piecePosition.pieceIndex + 1;
        if (next >= Race.track.pieces.Count) next -= Race.track.pieces.Count - 1;

        int nextNext = data.piecePosition.pieceIndex + 2;
        if (nextNext >= Race.track.pieces.Count) nextNext -= Race.track.pieces.Count - 1;

        int nextCorner = FindNextCorner(data.piecePosition.pieceIndex);

        Piece piece = Race.track.pieces[data.piecePosition.pieceIndex];
        Piece nextPiece = Race.track.pieces[next];
        Piece nextNextPiece = Race.track.pieces[nextNext];
        Piece corner = Race.track.pieces[nextCorner];

        Distance = piece.distance + data.piecePosition.inPieceDistance;
        Speed = Distance - _lastPos;
        if (Speed < 0) Speed = _lastSpeed;

        // Handle Switching
        if (_lastIndex != data.piecePosition.pieceIndex && Race.track.pieces[_lastIndex].@switch.HasValue)
        {
            int i = FindNextSwitch(data.piecePosition.pieceIndex);
            CurrentLane = Race.track.pieces[i].desiredLane;
            _lowPriority.Clear();
            _lowPriority.Enqueue(new Switch(CurrentLane == 1 ? "Right" : "Left"));
        }

        double angle = Math.Abs(data.angle);

        if (angle - _lastAngle > 0 && piece.angle.HasValue && piece.radius < 100) //1.75
            CurrentThrottle = 1 - (7 / Speed);
        else if (piece.angle.HasValue && piece.angle.Value <= 22.5) //1.75
        {
            if (angle - _lastAngle > 2.2)
                CurrentThrottle = 1 - (3.5 / Speed);
            else if (angle - _lastAngle > 1.4 && piece.radius.Value <= 200) //1.75
                CurrentThrottle = 1 - (5 / Speed);
            else
                CurrentThrottle *= 2;
        }
        else if (angle - _lastAngle > 1.4) //1.75
            CurrentThrottle = 1 - (5 / Speed);
        else if (corner.angle.HasValue && DistanceToPiece(Distance, nextCorner) <= 11 * Speed * (corner.radius < 100 ? 1.4 : 1.5))
            CurrentThrottle = 1 - ((Speed > 7 ? 7 : 6) / Speed);
        else
            CurrentThrottle *= 2;

        if (CurrentThrottle < 0.01)
            CurrentThrottle = 0.01;
        if (CurrentThrottle > 1)
            CurrentThrottle = 1;

        if (CurrentThrottle != _lastThrottle || Speed == 0)
        {
            _highPriority.Clear();
            _highPriority.Enqueue(new Throttle(CurrentThrottle));
        }

        _lastSpeed = Speed;
        _lastLane = CurrentLane;
        _lastThrottle = CurrentThrottle;
        _lastIndex = data.piecePosition.pieceIndex;
        _lastPos = Distance;
        _lastAngle = angle;
    }

    private void Tick()
    {
        if (_highPriority.Count > 0)
            send(_highPriority.Dequeue());
        else if (_lowPriority.Count > 0)
            send(_lowPriority.Dequeue());
        else
            send(new Ping());
    }

    private double DistanceToPiece(double distance, int index)
    {
        Piece p = Race.track.pieces[index];
        return p.distance < distance ? TrackLength - distance + p.distance : p.distance - distance;
    }

    private int FindNextSwitch(int index)
    {
        for (int i = index; i < Race.track.pieces.Count; i++)
        {
            if (Race.track.pieces[i].@switch.HasValue)
                return i;
        }
        for (int i = 0; i < index; i++)
        {
            if (Race.track.pieces[i].@switch.HasValue)
                return i;
        }
        return 0;
    }

    private int FindNextCorner(int index)
    {
        for (int i = index; i < Race.track.pieces.Count; i++)
        {
            if (Race.track.pieces[i].angle.HasValue && Math.Abs(Race.track.pieces[i].angle.Value) > 25)
                return i;
        }
        for (int i = 0; i < index; i++)
        {
            if (Race.track.pieces[i].angle.HasValue && Math.Abs(Race.track.pieces[i].angle.Value) > 25)
                return i;
        }
        return 0;
    }

    private void CalculateFastestRoute()
    {
        int switchTrack = -1;
        double angle = 0;

        for (int i = 0; i < Race.track.pieces.Count; i++)
        {
            if (Race.track.pieces[i].angle.HasValue)
                angle += Race.track.pieces[i].angle.Value;

            if (Race.track.pieces[i].@switch.HasValue)
            {
                if (switchTrack != -1)
                {
                    for (int j = switchTrack; j < i; j++)
                        Race.track.pieces[j].desiredLane = angle < 0 ? 0 : 1;
                }
                angle = 0;
                switchTrack = i - 1;
            }
        }

        for (int j = switchTrack; j < Race.track.pieces.Count; j++)
            Race.track.pieces[j].desiredLane = angle < 0 ? 0 : 1;
    }

    private double CalculateRouteLength()
    {
        double distance = 0;
        double angle = 0;

        for (int i = 0; i < Race.track.pieces.Count; i++)
        {
            Piece p = Race.track.pieces[i];
            p.distance = distance;
            if (p.angle.HasValue)
                p.length = CalculateArcLength(Math.Abs(p.angle.Value), p.radius.Value + Race.track.lanes[p.desiredLane].distanceFromCenter);

            p.startAngle = angle;
            if (p.angle.HasValue) angle += Math.Abs(p.angle.Value);
            p.endAngle = angle;

            distance += p.length;
        }

        return distance;
    }

    private void send(SendMsg msg)
    {
        writer.WriteLine(msg.ToJson());
    }

    private double CalculateArcLength(double angle, double radius)
    {
        return angle / 360 * (2 * Math.PI * radius);
    }
}

#region JSON Objects
public class Piece
{
    public double length { get; set; }
    public bool? @switch { get; set; }
    public int? radius { get; set; }
    public double? angle { get; set; }

    public int desiredLane { get; set; }
    public double distance { get; set; }
    public double startAngle { get; set; }
    public double endAngle { get; set; }
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

public class BotId
{
    public string name { get; set; }
    public string key { get; set; }
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

public class LapTime
{
    public int lap { get; set; }
    public int ticks { get; set; }
    public int millis { get; set; }
}

public class RaceTime
{
    public int laps { get; set; }
    public int ticks { get; set; }
    public int millis { get; set; }
}

public class Ranking
{
    public int overall { get; set; }
    public int fastestLap { get; set; }
}

public class LapData
{
    public CarData car { get; set; }
    public LapTime lapTime { get; set; }
    public RaceTime raceTime { get; set; }
    public Ranking ranking { get; set; }
}

public class CarData
{
    public string name { get; set; }
    public string color { get; set; }
}

public class SpawnObject
{
    public string msgType { get; set; }
    public CarData data { get; set; }
    public string gameId { get; set; }
    public int gameTick { get; set; }
}

public class CrashObject
{
    public string msgType { get; set; }
    public CarData data { get; set; }
    public string gameId { get; set; }
    public int gameTick { get; set; }
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

public class CarObject
{
    public string msgType { get; set; }
    public CarData data { get; set; }
}

public class LapObject
{
    public string msgType { get; set; }
    public LapData data { get; set; }
    public string gameId { get; set; }
    public int gameTick { get; set; }
}
#endregion

#region Messages
class MsgWrapper
{
    public string msgType;
    public Object data;

    public MsgWrapper(string msgType, Object data)
    {
        this.msgType = msgType;
        this.data = data;
    }
}

abstract class SendMsg
{
    public string ToJson()
    {
        return JsonConvert.SerializeObject(new MsgWrapper(this.MsgType(), this.MsgData()));
    }
    protected virtual Object MsgData()
    {
        return this;
    }

    public abstract string MsgType();
}

class Join : SendMsg
{
    public string name;
    public string key;
    public string color;

    public Join(string name, string key)
    {
        this.name = name;
        this.key = key;
        this.color = "orange";
    }

    public override string MsgType()
    {
        return "join";
    }
}

class JoinRace : SendMsg
{
    public BotId botId;
    public string trackName;
    public int carCount;

    public JoinRace(string name, string key, string track = "keimola", int opponents = 0)
    {
        this.botId = new BotId();
        this.botId.name = name;
        this.botId.key = key;
        this.trackName = track;
        this.carCount = opponents + 1;
    }

    public override string MsgType()
    {
        return "joinRace";
    }
}

class Ping : SendMsg
{
    public override string MsgType()
    {
        return "ping";
    }
}

class Throttle : SendMsg
{
    public double value;

    public Throttle(double value)
    {
        this.value = value;
    }

    protected override Object MsgData()
    {
        return this.value;
    }

    public override string MsgType()
    {
        return "throttle";
    }
}

class Switch : SendMsg
{
    public string value;

    public Switch(string value)
    {
        this.value = value;
    }

    protected override Object MsgData()
    {
        return this.value;
    }

    public override string MsgType()
    {
        return "switchLane";
    }
}
#endregion