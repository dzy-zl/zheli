using Zheli.Domain;
using Zheli.Storage;

internal static class PetDesktopTests
{
    public static void Run(Action<string,Action> test,string directory)
    {
        static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new InvalidDataException($"expected {expected}, got {actual}");}
        var work=new PetArea(0,0,2560,1560);
        test("pet default placement respects 150 percent pixel size",()=>Equal(new PetPoint(2308,1299),PetDesktop.Restore(null,work,234,243,18)));
        test("pet placement supports negative monitor origin",()=>Equal(new PetPoint(-168,866),PetDesktop.Restore(null,new(-1920,0,1920,1040),156,162,12)));
        test("pet placement supports monitor above main",()=>Equal(new PetPoint(1388,-212),PetDesktop.Restore(null,new(0,-1200,1600,1200),200,200,12)));
        test("pet placement capture roundtrip",()=>{var point=new PetPoint(1073,531);var p=PetDesktop.Capture("screen",point,work,234,243,18);Equal(point,PetDesktop.Restore(p,work,234,243,18));});
        test("pet ratio survives work area change",()=>Equal(new PetPoint(843,400),PetDesktop.Restore(new("screen",.5,.5),new(0,0,1920,1043),234,243,18)));
        test("pet changed DPI keeps entire window visible",()=>{var p=PetDesktop.Capture("screen",new(2308,1299),work,234,243,18);Equal(new PetPoint(2224,1212),PetDesktop.Restore(p,work,312,324,24));});
        test("pet tiny work area remains reachable",()=>Equal(new PetPoint(0,0),PetDesktop.Restore(null,new(0,0,100,90),200,200,20)));
        test("pet invalid ratios fall back and clamp",()=>Equal(new PetPoint(12,1028),PetDesktop.Restore(new("screen",-4,double.NaN),new(0,0,1600,1200),200,160,12)));
        test("pet offscreen capture clamps to visible ratios",()=>{var p=PetDesktop.Capture("screen",new(-500,9000),work,234,243,18);Equal(0d,p.XRatio);Equal(1d,p.YRatio);});
        test("pet nearby edge snaps without crossing work area",()=>Equal(new PetPoint(18,18),PetDesktop.Constrain(new(30,-100),work,234,243,18,36)));
        test("pet interior does not snap",()=>Equal(new PetPoint(500,600),PetDesktop.Constrain(new(500,600),work,234,243,18,36)));
        var displays=new[]{new PetDisplay("secondary",new(-1920,0,1920,1040)),new PetDisplay("primary",work,true)};
        test("pet saved monitor selected case insensitively",()=>Equal("secondary",PetDesktop.SelectDisplay(displays,"SECONDARY").Name));
        test("pet removed monitor falls back to primary",()=>Equal("primary",PetDesktop.SelectDisplay(displays,"disconnected").Name));
        test("pet small pointer jitter remains a click",()=>{var g=new PetPointerGesture();g.Begin(new(100,100),6,6);Equal(false,g.Move(new(105,102)));Equal(true,g.Release(new(103,101)));});
        test("pet drag starts once and never becomes click",()=>{var g=new PetPointerGesture();g.Begin(new(100,100),6,6);Equal(true,g.Move(new(106,100)));Equal(false,g.Move(new(130,130)));Equal(false,g.Release(new(100,100)));});
        test("pet distant release without move event is not click",()=>{var g=new PetPointerGesture();g.Begin(new(100,100),6,6);Equal(false,g.Release(new(300,100)));});
        test("pet lost capture cancels pending click",()=>{var g=new PetPointerGesture();g.Begin(new(100,100),6,6);g.Cancel();Equal(false,g.Release(new(100,100)));});
        test("pet release without press is ignored",()=>Equal(false,new PetPointerGesture().Release(new(0,0))));
        test("pet new click works after cancelled drag",()=>{var g=new PetPointerGesture();g.Begin(new(0,0),6,6);g.Move(new(100,0));g.Cancel();g.Begin(new(100,0),6,6);Equal(true,g.Release(new(100,0)));});
        test("pet local position and hidden state persist",()=>
        {
            var path=Path.Combine(directory,"pet-window.json");
            var state=new PetLocalState{Placement=new("secondary",.3,.7),HiddenByUser=true,LastShowPet=false,LastReduceMotion=true};
            new DraftStore<PetLocalState>(path).Save(state);Equal(state,new DraftStore<PetLocalState>(path).Load());
        });
    }
}
