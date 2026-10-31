using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Petty.App.Sprites;
using Petty.Core.Runtime;

internal static class PlayPreview
{
    // Uses the real scene sampler and cached WPF sprite frames, not a second
    // JavaScript implementation of the animation under review.
    public static void Write(IReadOnlyList<SpriteVariant> variants, string output)
    {
        var directory = Path.Combine(output, "play");
        Directory.CreateDirectory(directory);
        var ball = PropImage.Load(Path.Combine(SpriteAssetLocator.FindSharedDirectory()!, "ball.png"));
        var previews = new List<object>();
        var times = new[] { 0.0, 0.3, 0.45, 0.6, 0.75, 0.9 };
        var board = new DrawingVisual();
        using (var drawing = board.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 34, 44)), null, new Rect(0, 0, 1920, 8 * 195));
            var row = 0;
            foreach (var variant in variants.Where(v => v.IsDefault))
            {
                var sheet = SpriteSheetLoader.Load(variant.Directory, variant.AssetId);
                var clip = sheet.GetClip("play");
                var animation = new Petty.App.Sprites.AnimationController(sheet);
                animation.Play("play");
                var contactX = sheet.Metadata.PlayContactX ?? 58;
                var contactY = sheet.Metadata.PlayContactY ?? 45;
                var lift = Math.Max(0, (sheet.GroundRow - contactY) * 3 - 48 * 0.44);
                var frames = clip.Frames.Select(f => Encode(f.Image)).ToArray();
                var samples = new List<object>();
                for (var i = 0; i <= 300; i++)
                {
                    var t = i / 50.0;
                    var pose = ActionScene.SamplePlay(t, 6, clip.Frames.Count, clip.FramesPerSecond, 700, lift);
                    animation.ShowSceneFrame(pose.FrameIndex);
                    if (animation.CurrentFrame != clip[pose.FrameIndex]) throw new InvalidOperationException("Play frame clock mismatch.");
                    samples.Add(new { frame = pose.FrameIndex, x = pose.PropOffset, y = pose.PropHeight });
                }
                previews.Add(new { id = variant.BreedId, frames, contactX, groundRow = sheet.GroundRow, samples });
                for (var column = 0; column < times.Length; column++)
                {
                    var t = times[column];
                    var pose = ActionScene.SamplePlay(t, 6, clip.Frames.Count, clip.FramesPerSecond, 700, lift);
                    drawing.PushTransform(new TranslateTransform(column * 320, row * 195));
                    drawing.DrawText(new FormattedText($"{variant.BreedId} / {t:0.00}s", CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Consolas"), 11, Brushes.Wheat, 1), new Point(8, 8));
                    const double floor = 175;
                    drawing.DrawLine(new Pen(Brushes.SlateGray, 1), new Point(0, floor), new Point(315, floor));
                    drawing.DrawImage(clip[pose.FrameIndex].Image, new Rect(0, floor - (sheet.GroundRow + 1) * 3, 192, 192));
                    var x = contactX * 3 + 48 * 0.44 + pose.PropOffset;
                    var y = floor - 21 + pose.PropHeight;
                    drawing.PushTransform(new RotateTransform(pose.PropOffset / (48 * 0.44) * 180 / Math.PI, x, y));
                    drawing.DrawImage(ball.Image, new Rect(x - 24, y - 24, 48, 48));
                    drawing.Pop();
                    drawing.Pop();
                }
                row++;
            }
        }
        RenderOptions.SetBitmapScalingMode(board, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(board, EdgeMode.Aliased);
        var image = new RenderTargetBitmap(1920, 8 * 195, 96, 96, PixelFormats.Pbgra32);
        image.Render(board);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(Path.Combine(directory, "contact-sheet.png"))) png.Save(file);
        var data = JsonSerializer.Serialize(new { ball = Encode(ball.Image), previews });
        File.WriteAllText(Path.Combine(directory, "preview.html"), Html.Replace("/*DATA*/", data));
    }

    private static string Encode(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private const string Html = """
        <!doctype html><html lang="en"><meta charset="utf-8"><title>Petty — play scene</title>
        <style>body{background:#1e222c;color:#ede3cf;font:15px system-ui;margin:32px}button,select{font:inherit;padding:8px;margin:8px}canvas{image-rendering:pixelated;border:1px solid #50586a;background:#323a47}p{color:#b3bac8}</style>
        <h1>Play scene / real runtime samples</h1><select id="breed"></select><button id="pause">Pause</button><button id="step">Next frame</button><label><input type="checkbox" id="flip">Face left</label><br>
        <canvas width="1000" height="230"></canvas><p id="time"></p><p>Shared scene clock, paw contact, small hops, rolling rotation, final departure. 6 seconds, 50 samples/s. Pixel art is embedded; no network needed.</p>
        <script>const data=/*DATA*/;const select=document.querySelector('#breed'),canvas=document.querySelector('canvas'),ctx=canvas.getContext('2d');
        for(const v of data.previews){select.add(new Option(v.id,v.id));v.images=v.frames.map(src=>{const im=new Image();im.src=src;return im})}const ball=new Image();ball.src=data.ball;
        let elapsed=0,last=0,paused=false;select.onchange=()=>{elapsed=0};document.querySelector('#pause').onclick=()=>{paused=!paused};document.querySelector('#step').onclick=()=>{paused=true;elapsed=(elapsed+0.02)%6};
        function draw(t){if(last&&!paused)elapsed=(elapsed+(t-last)/1000)%6;last=t;const v=data.previews.find(v=>v.id===select.value),s=v.samples[Math.min(300,Math.floor(elapsed*50))];ctx.clearRect(0,0,1000,230);ctx.imageSmoothingEnabled=false;ctx.save();if(document.querySelector('#flip').checked){ctx.translate(1000,0);ctx.scale(-1,1)}const floor=195,petX=50;ctx.strokeStyle='#687287';ctx.beginPath();ctx.moveTo(0,floor);ctx.lineTo(1000,floor);ctx.stroke();if(v.images[s.frame].complete)ctx.drawImage(v.images[s.frame],petX,floor-(v.groundRow+1)*3,192,192);const x=petX+v.contactX*3+48*0.44+s.x,y=floor-21+s.y;ctx.translate(x,y);ctx.rotate(s.x/(48*0.44));if(ball.complete)ctx.drawImage(ball,-24,-24,48,48);ctx.restore();document.querySelector('#time').textContent=`${elapsed.toFixed(2)} s / sprite frame ${s.frame}`;requestAnimationFrame(draw)}requestAnimationFrame(draw);
        </script></html>
        """;
}
