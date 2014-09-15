using Newtonsoft.Json;
using System;

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