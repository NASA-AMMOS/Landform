set mstest="C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"
%mstest% /Parallel^
 CloudTest\bin\Release\CloudTest.dll^
 GeometryTest\bin\Release\GeometryTest.dll^
 GeometryThirdpartyTest\bin\Release\GeometryThirdpartyTest.dll^
 ImagingTest\bin\Release\ImagingTest.dll^
 ImagingEmguTest\bin\Release\ImagingEmguTest.dll^
 MathExtensionsTest\bin\Release\MathExtensionsTest.dll^
 PipelineTest\bin\Release\PipelineTest.dll^
 UtilTest\bin\Release\UtilTest.dll^
 XnaTest\bin\Release\XnaTest.dll