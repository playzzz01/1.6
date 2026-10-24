@echo off
setlocal
call "D:\Vs\Vis\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b 1
set ROOT=%~dp0
set OUT=%ROOT%..\..\outputs
if not exist "%ROOT%build" mkdir "%ROOT%build"
pushd "%ROOT%build"
set "MH=%ROOT%minhook"
set "IM=%ROOT%imgui"
cl /nologo /c /O2 /MD /TC /I"%MH%\include" /I"%MH%\src" "%MH%\src\buffer.c" "%MH%\src\hook.c" "%MH%\src\trampoline.c" "%MH%\src\hde\hde32.c"
if errorlevel 1 exit /b 2
cl /nologo /c /O2 /MD /EHsc /std:c++17 /utf-8 /I"%MH%\include" /I"%IM%" /I"%IM%\backends" "%ROOT%internal.cpp" "%IM%\imgui.cpp" "%IM%\imgui_draw.cpp" "%IM%\imgui_tables.cpp" "%IM%\imgui_widgets.cpp" "%IM%\backends\imgui_impl_win32.cpp" "%IM%\backends\imgui_impl_opengl2.cpp"
if errorlevel 1 exit /b 3
link /nologo /dll /machine:x86 /out:"%OUT%\CsInternal.dll" internal.obj imgui.obj imgui_draw.obj imgui_tables.obj imgui_widgets.obj imgui_impl_win32.obj imgui_impl_opengl2.obj buffer.obj hook.obj trampoline.obj hde32.obj user32.lib gdi32.lib opengl32.lib
if errorlevel 1 exit /b 4
cl /nologo /O2 /MD /EHsc /std:c++17 /utf-8 "%ROOT%loader.cpp" /link /out:"%OUT%\CsInternalLoader.exe" /machine:x86 user32.lib
if errorlevel 1 exit /b 5
popd
echo Built %OUT%\CsInternal.dll and CsInternalLoader.exe

