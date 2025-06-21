curl -OL https://github.com/luau-lang/luau/releases/latest/download/luau-macos.zip
unzip ./luau-macos.zip
mv ./luau ./tools/osx/.
mv ./luau-ast ./tools/osx/.
mv ./luau-analyze ./tools/osx/.
mv ./luau-compile ./tools/osx/.
rm ./luau-macos.zip

curl -OL https://github.com/luau-lang/luau/releases/latest/download/luau-ubuntu.zip
unzip ./luau-ubuntu.zip
mv ./luau ./tools/linux/.
mv ./luau-ast ./tools/linux/.
mv ./luau-analyze ./tools/linux/.
mv ./luau-compile ./tools/linux/.
rm ./luau-ubuntu.zip

curl -OL https://github.com/luau-lang/luau/releases/latest/download/luau-windows.zip
unzip ./luau-windows.zip
mv ./luau.exe ./tools/win/.
mv ./luau-ast.exe ./tools/win/.
mv ./luau-analyze.exe ./tools/win/.
mv ./luau-compile.exe ./tools/win/.
rm ./luau-windows.zip