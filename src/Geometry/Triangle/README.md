
Triangle.NET code by Christian Woltering, http://triangle.codeplex.com/

This copy was sourced from https://github.com/garykac/triangle.net on 9/23/25

Triangle.NET is unfortunately not well maintained anymore.  We use it (only) to compute a Delanuay triangulation of a 2D point set.

Unfortunately the latest package available on nuget has a transitive depencency on an old version of `System.Runtime.InteropServices.PInvoke` which causes gnarly build errors in `GeometryThirdParty/UVAtlas.cs`.  So instead of depending on the nuget package we now just include the sourcecode under `src/Geometry/Triangle/`.

```
cd src/GeometryThirdParty
dotnet nuget why System.Runtime.InteropServices.PInvoke

Geometry (v1.0.0)
└─ Triangle (v0.0.6-Beta3)
   └─ NETStandard.Library (v1.5.0-rc2-24027)
      ├─ System.IO.Compression (v4.1.0-rc2-24027)
      │  └─ System.Runtime.InteropServices (v4.1.0-rc2-24027)
      │     └─ System.Runtime.InteropServices.PInvoke (v4.0.0-rc2-24027)
```


