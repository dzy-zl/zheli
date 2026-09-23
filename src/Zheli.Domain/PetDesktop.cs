namespace Zheli.Domain;

// All geometry is expressed in physical pixels, including negative monitor origins.
public readonly record struct PetPoint(int X,int Y);
public readonly record struct PetArea(int Left,int Top,int Width,int Height);
public sealed record PetDisplay(string Name,PetArea WorkArea,bool Primary=false);
public sealed record PetPlacement(string? Display,double XRatio=1,double YRatio=1);
public sealed record PetLocalState
{
    public int Version { get; init; }=1;
    public PetPlacement? Placement { get; init; }
    public bool HiddenByUser { get; init; }
    // Cache only: global settings are owned and changed by Zheli Settings.
    public bool LastShowPet { get; init; }=true;
    public bool LastReduceMotion { get; init; }
}

public static class PetDesktop
{
    public static PetDisplay SelectDisplay(IReadOnlyList<PetDisplay> displays,string? savedName)
    {
        if(displays.Count==0)throw new ArgumentException("没有可用显示器。");
        return displays.FirstOrDefault(x=>string.Equals(x.Name,savedName,StringComparison.OrdinalIgnoreCase))
            ??displays.FirstOrDefault(x=>x.Primary)??displays[0];
    }
    private static (int min,int max) Range(int origin,int length,int petSize,int margin)
    {
        if(length<=0||petSize<=0)throw new ArgumentOutOfRangeException(nameof(length));
        var padding=Math.Min(Math.Max(0,margin),Math.Max(0,(length-petSize)/2));
        return (origin+padding,origin+padding+Math.Max(0,length-petSize-padding*2));
    }
    private static double Ratio(double value)=>double.IsFinite(value)?Math.Clamp(value,0,1):1;
    public static PetPoint Restore(PetPlacement? placement,PetArea work,int width,int height,int margin)
    {
        var x=Range(work.Left,work.Width,width,margin);var y=Range(work.Top,work.Height,height,margin);
        return new(x.min+(int)Math.Round((x.max-x.min)*Ratio(placement?.XRatio??1)),
            y.min+(int)Math.Round((y.max-y.min)*Ratio(placement?.YRatio??1)));
    }
    public static PetPoint Constrain(PetPoint position,PetArea work,int width,int height,int margin,int horizontalSnap=0)
    {
        var x=Range(work.Left,work.Width,width,margin);var y=Range(work.Top,work.Height,height,margin);
        var left=Math.Clamp(position.X,x.min,x.max);var top=Math.Clamp(position.Y,y.min,y.max);
        if(horizontalSnap>0)
        {
            if(left-x.min<horizontalSnap)left=x.min;
            else if(x.max-left<horizontalSnap)left=x.max;
        }
        return new(left,top);
    }
    public static PetPlacement Capture(string display,PetPoint position,PetArea work,int width,int height,int margin)
    {
        var x=Range(work.Left,work.Width,width,margin);var y=Range(work.Top,work.Height,height,margin);
        return new(display,x.max==x.min?1:Ratio((position.X-x.min)/(double)(x.max-x.min)),
            y.max==y.min?1:Ratio((position.Y-y.min)/(double)(y.max-y.min)));
    }
}

public sealed class PetPointerGesture
{
    private PetPoint _start;
    private double _thresholdX,_thresholdY;
    private bool _pressed,_dragged;
    public void Begin(PetPoint point,double thresholdX,double thresholdY)
    { _start=point;_thresholdX=Math.Max(1,thresholdX);_thresholdY=Math.Max(1,thresholdY);_pressed=true;_dragged=false; }
    public bool Move(PetPoint point)
    {
        if(!_pressed||_dragged)return false;
        if(Math.Abs((long)point.X-_start.X)<_thresholdX&&Math.Abs((long)point.Y-_start.Y)<_thresholdY)return false;
        _dragged=true;return true;
    }
    public bool Release(PetPoint point)
    { Move(point);var click=_pressed&&!_dragged;Cancel();return click; }
    public void Cancel(){_pressed=false;_dragged=false;}
}
