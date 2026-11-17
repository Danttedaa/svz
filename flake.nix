{
  description = "Samurai vs Zombies Defense 3.4.0 -> Godot 4 (C#) arm64 port";

  # Same nixpkgs as the system, so most packages are already in the store.
  inputs.nixpkgs.url = "github:NixOS/nixpkgs/d4f25a3e562008648d45bcf2d70bcc7422cad84e";

  outputs = { self, nixpkgs }:
    let
      system = "x86_64-linux";
      pkgs = import nixpkgs {
        inherit system;
        config = { allowUnfree = true; android_sdk.accept_license = true; };
      };

      godotPkg = pkgs.godot_4-mono;
      templates = godotPkg.export-templates-bin;
      # Godot 4.7 Android templates need net9.0, but the nixpkgs wrapper prepends dotnet 8 to PATH.
      # Call the inner binary (it only sets NUGET_FALLBACK_PACKAGES) with dotnet 9 on PATH.
      dotnet = pkgs.dotnet-sdk_9;
      godot = pkgs.writeShellScriptBin "godot" ''
        export PATH=${dotnet}/bin:$PATH
        exec ${godotPkg.unwrapped}/libexec/godot.linuxbsd.editor.x86_64.mono "$@"
      '';
      jdk = pkgs.jdk17;

      android = pkgs.androidenv.composeAndroidPackages {
        platformVersions = [ "35" ];
        buildToolsVersions = [ "35.0.0" ];
        includeNDK = false;
        includeEmulator = false;
        includeSystemImages = false;
      };
      androidSdk = "${android.androidsdk}/libexec/android-sdk";

      python = pkgs.python3;
    in {
      devShells.${system}.default = pkgs.mkShell {
        packages = [
          godot dotnet jdk android.androidsdk android.platform-tools
          python pkgs.uv pkgs.jadx pkgs.apktool  # ilspycmd: nix shell nixpkgs#ilspycmd (it pulls dotnet 8)
        ];

        DOTNET_ROOT = "${dotnet.unwrapped}/share/dotnet";
        DOTNET_CLI_TELEMETRY_OPTOUT = "1";
        JAVA_HOME = jdk.home;
        ANDROID_HOME = androidSdk;
        ANDROID_SDK_ROOT = androidSdk;
        UV_PYTHON = "${python}/bin/python3";
        UV_PYTHON_DOWNLOADS = "never";
        # decompiler for the game/Unity DLLs (called by path: its wrapper brings its own dotnet 8)
        SVZ_ILSPYCMD = "${pkgs.ilspycmd}/bin/ilspycmd";
        # native wheels in the asset-tool venv (texture decoders) need libstdc++
        LD_LIBRARY_PATH = "${pkgs.stdenv.cc.cc.lib}/lib";

        shellHook = ''
          root=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
          # Godot reads templates/editor settings from XDG dirs: keep them inside the project.
          export XDG_DATA_HOME=$root/.nixenv/share XDG_CONFIG_HOME=$root/.nixenv/config XDG_CACHE_HOME=$root/.nixenv/cache
          export NUGET_PACKAGES=$root/.nixenv/nuget
          mkdir -p $XDG_DATA_HOME/godot/export_templates $XDG_CONFIG_HOME/godot
          ln -sfn ${templates}/share/godot/export_templates/4.7.2.stable.mono \
            $XDG_DATA_HOME/godot/export_templates/4.7.2.stable.mono

          # signing key: keep keys/svz.jks safe, Android only updates apps signed with the same key
          mkdir -p $root/keys
          ks=$root/keys/svz.jks
          export GODOT_ANDROID_KEYSTORE_RELEASE_PATH=$ks GODOT_ANDROID_KEYSTORE_RELEASE_USER=svz GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD=android
          [ -f $ks ] || keytool -genkeypair -keystore $ks -storepass android -keypass android \
            -alias svz -dname "CN=SvZ Port" -keyalg RSA -keysize 2048 -validity 36500 >/dev/null 2>&1

          cat > $XDG_CONFIG_HOME/godot/editor_settings-4.7.tres <<EOF
          [gd_resource type="EditorSettings" format=3]
          [resource]
          export/android/android_sdk_path = "${androidSdk}"
          export/android/java_sdk_path = "${jdk.home}"
          export/android/debug_keystore = "$ks"
          export/android/debug_keystore_user = "svz"
          export/android/debug_keystore_pass = "android"
          EOF

          # Asset tools (UnityPy is not in nixpkgs): pinned venv, Python from Nix.
          if [ ! -x $root/.venv/bin/python ] || [ $root/tools/requirements.txt -nt $root/.venv/.stamp ]; then
            uv venv -q $root/.venv && uv pip install -q --python $root/.venv/bin/python -r $root/tools/requirements.txt \
              && touch $root/.venv/.stamp
          fi
        '';
      };
    };
}
