using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

internal static partial class Sdk {
    /// <summary>
    /// Registers YAML helpers in sdk.text.yaml.
    /// </summary>
    private static void YamlModules(LuaWorld _LuaWorld) {
        // sdk.text.yaml.encode(value, opts)
        // opts is accepted for API parity with sdk.text.json.encode, but formatting is currently default.
        _LuaWorld.Sdk.Text.Yaml["encode"] = (System.Func<DynValue, DynValue, string>)((val, opts) => {
            object? obj = Lua.Globals.Utils.FromDynValue(val);
            return Core.Serialization.Yaml.YamlHelpers.WriteDocument(obj);
        });

        // sdk.text.yaml.decode(yaml)
        _LuaWorld.Sdk.Text.Yaml["decode"] = (System.Func<string, DynValue>)((yaml) => {
            try {
                object plain = Core.Serialization.Yaml.YamlHelpers.ParseDocumentToPlainObject(yaml);
                return Lua.Globals.Utils.ToDynValue(_LuaWorld.LuaScript, plain);
            } catch (Exception ex) {
                Core.Diagnostics.LuaInternalCatch("sdk.text.yaml.decode failed: " + ex);
                return DynValue.Nil;
            }
        });

        // sdk.text.yaml.read_file(path)
        _LuaWorld.Sdk.Text.Yaml["read_file"] = (string path) => {
            object? obj = ScriptEngines.Global.SdkModule.Helpers.AddYamlHelpers.Yaml_Read_File(path);
            return obj == null ? DynValue.Nil : Lua.Globals.Utils.ToDynValue(_LuaWorld.LuaScript, obj);
        };

        // sdk.text.yaml.write_file(path, value)
        _LuaWorld.Sdk.Text.Yaml["write_file"] = (string path, DynValue value) => {
            object? obj = Lua.Globals.Utils.FromDynValue(value);
            ScriptEngines.Global.SdkModule.Helpers.AddYamlHelpers.Yaml_Write_File(path, obj);
        };

        // Root aliases to match sdk.toml_* convenience helpers.
        _LuaWorld.Sdk.Table["yaml_read_file"] = _LuaWorld.Sdk.Text.Yaml["read_file"];
        _LuaWorld.Sdk.Table["yaml_write_file"] = _LuaWorld.Sdk.Text.Yaml["write_file"];
    }
}
