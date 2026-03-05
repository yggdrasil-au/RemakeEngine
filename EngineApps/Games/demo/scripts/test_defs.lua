-- test moonsharp specific features

-- 1. _MOONSHARP table
print("Testing _MOONSHARP table:")
print("  Version: " .. tostring(_MOONSHARP.version))
print("  Lua Compat: " .. tostring(_MOONSHARP.luacompat))
print("  Platform: " .. tostring(_MOONSHARP.platform))
print("  Is AOT: " .. tostring(_MOONSHARP.is_aot))
print("  Is Unity: " .. tostring(_MOONSHARP.is_unity))
print("  Is Mono: " .. tostring(_MOONSHARP.is_mono))
print("  Is CLR4: " .. tostring(_MOONSHARP.is_clr4))
print("  Is PCL: " .. tostring(_MOONSHARP.is_pcl))
print("  Banner: " .. tostring(_MOONSHARP.banner))

-- 2. New functions in global namespace
print("\nTesting Global Namespace Additions:")
local packed = pack(1, 2, 3)
print("  pack exists: " .. tostring(type(pack) == "function"))
print("  unpack exists: " .. tostring(type(unpack) == "function"))
print("  loadsafe exists: " .. tostring(type(loadsafe) == "function"))
print("  loadfilesafe exists: " .. tostring(type(loadfilesafe) == "function"))

-- 3. String module additions
print("\nTesting 'string' module additions:")
print("  unicode: " .. tostring(string.unicode("A")))
print("  contains: " .. tostring(string.contains("Hello World", "World")))
print("  startsWith: " .. tostring(string.startsWith("Hello World", "Hello")))
print("  endsWith: " .. tostring(string.endsWith("Hello World", "World")))

-- 4. Dynamic module
print("\nTesting 'dynamic' module:")
if dynamic then
    local prep = dynamic.prepare("1 + 2")
    print("  eval: " .. tostring(dynamic.eval(prep)))
else
    print("  dynamic module not available")
end

-- 5. JSON module
print("\nTesting 'json' module (MoonSharp vs Engine):")

-- Test global 'json' (if enabled)
if json then
    print("  MoonSharp 'json' module:")
    print("    json.serialize is: " .. type(json.serialize))
    print("    json.null is: " .. type(json.null))
    print("    json.parse is: " .. type(json.parse))
    print("    json.isNull is: " .. type(json.isNull))

    local status, js = pcall(function() return json.serialize({a = 1, b = json.null()}) end)
    if status then
        print("      serialize: " .. js)
        local status2, tbl = pcall(function() return json.parse(js) end)
        if status2 then
            print("      parse: table returned")
            if type(json.isNull) == "function" then
                print("      isNull check: " .. tostring(json.isNull(tbl.b)))
            else
                print("      isNull: (nil) - function not available in this environment")
            end
        else
            print("      parse failed: " .. tostring(tbl))
        end
    end
else
    print("  MoonSharp 'json' module not available")
end

-- Test 'sdk.text.json' (Engine provided)
if sdk and sdk.text and sdk.text.json then
    print("\n  Engine 'sdk.text.json' module:")
    print("    sdk.text.json.encode is: " .. type(sdk.text.json.encode))
    print("    sdk.text.json.decode is: " .. type(sdk.text.json.decode))
    print("    sdk.text.json.isNull is: " .. type(sdk.text.json.isNull))

    local data = { name = "RemakeEngine", version = 6.9, is_active = true, data = nil }
    local js = sdk.text.json.encode(data, { indent = true })
    print("    encode:\n" .. js)

    local decoded = sdk.text.json.decode(js)
    print("    decode: table returned")
    print("    isNull check (data): " .. tostring(sdk.text.json.isNull(decoded.data)))
    print("    isNull check (name): " .. tostring(sdk.text.json.isNull(decoded.name)))
end

-- 6. Language differences
print("\nTesting Language differences:")
-- Lambda style
-- Note: MoonSharp supports this, but let's wrap it in a pcall or comment if standard Lua parsers in some IDEs complain,
-- but here we are testing the runtime. The error "attempt to call a nil value" appeared AFTER JSON module.
-- Let's check if the Lambda style or Unicode escapes caused the previous failure by adding safety or comments.

print("  Testing Unicode escape \\u{20AC}:")
local status, err = pcall(function()
    print("    Result: " .. "\u{20AC}")
end)
if not status then print("    Unicode escape failed: " .. tostring(err)) end

print("  Testing Lambda style:")
local status_lambda, err_lambda = pcall(function()
    -- We use load to check if the syntax is accepted by the loader
    local func = load("return |x, y| x + y")
    if func then
        local sum = func()
        print("    Lambda style sum(5, 10): " .. sum(5, 10))
    else
        print("    Lambda style syntax not supported by 'load'")
    end
end)
if not status_lambda then print("    Lambda test threw error: " .. tostring(err_lambda)) end
