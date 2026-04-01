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
    local jsonIsNullMember = rawget(json, "isNull")
    print("    json.isNull is: " .. tostring(jsonIsNullMember) .. " (should be nil; use sdk.text.json.isNull)")

    local status, js = pcall(function() return json.serialize({a = 1, b = json.null()}) end)
    if status then
        print("      serialize: " .. js)
        local status2, tbl = pcall(function() return json.parse(js) end)
        if status2 and tbl then
            print("      parse: table returned")
            local nulval = json.null()
            if jsonIsNullMember == nil then
                print("      isNull: (nil) - not available in this environment; use sdk.text.json.isNull")
            else
                print("      isNull: unexpected non-nil value: " .. tostring(jsonIsNullMember))
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

-- Unicode escape
print("  Testing Unicode escape \\u{20AC}:")
local status, err = pcall(function()
    local euro = "\u{20AC}"
    print("    Result: " .. euro .. " (codepoint: " .. tostring(string.unicode(euro)) .. ")")
    assert(string.unicode(euro) == 8364, "Unicode escape did not produce expected codepoint")
end)
if not status then print("    Unicode escape failed: " .. tostring(err)) end

-- Lambda style
print("  Testing Lambda style:")
local status_lambda, err_lambda = pcall(function()
    local func = load("return |x, y| x + y")
    if func then
        local sum = func()
        print("    Lambda style sum(5, 10): " .. sum(5, 10))
        assert(sum(5, 10) == 15, "Lambda did not return expected value")
    else
        print("    Lambda style syntax not supported by 'load'")
    end
end)
if not status_lambda then print("    Lambda test threw error: " .. tostring(err_lambda)) end

-- Multiple-expression indices parsing/runtime behavior
print("  Testing multiple-expression indices:")
local multiIndexChunk, multiIndexLoadErr = load("local x = {}; return x[1,2,3]")
if multiIndexChunk then
    print("    Parse accepted: true")
    local multiIndexRunOk, multiIndexRunResult = pcall(multiIndexChunk)
    print("    Runtime success: " .. tostring(multiIndexRunOk))
    if not multiIndexRunOk then
        print("    Runtime error (expected for non-userdata): " .. tostring(multiIndexRunResult))
    else
        print("    Runtime result: " .. tostring(multiIndexRunResult))
    end
else
    print("    Parse accepted: false")
    print("    Parse error: " .. tostring(multiIndexLoadErr))
end

-- __iterator metamethod and default iterator (probe in protected chunks)
print("  Testing __iterator and default iterator:")

local function hasArg(flag)
    if type(argv) ~= "table" then
        return false
    end

    for i = 1, #argv do
        if argv[i] == flag then
            return true
        end
    end

    return false
end

local runUnsafeIteratorProbe = hasArg("--unsafe-iterator-probes")

local defaultIteratorStatus, defaultIteratorResult = pcall(function()
    local chunk, loadErr = load([[local t = {10, 20, 30}
local out = {}
for v in t do
    table.insert(out, v)
end
return out]])
    if not chunk then
        error("default iterator chunk load failed: " .. tostring(loadErr))
    end
    return chunk()
end)
if defaultIteratorStatus and type(defaultIteratorResult) == "table" then
    print("    Default iterator values: " .. table.concat(defaultIteratorResult, ", "))
    if #defaultIteratorResult == 3 and defaultIteratorResult[1] == 10 and defaultIteratorResult[2] == 20 and defaultIteratorResult[3] == 30 then
        print("    Default iterator works as expected.")
    else
        print("    Default iterator returned unexpected values.")
    end
else
    print("    Default iterator unsupported/error: " .. tostring(defaultIteratorResult))
end

if not runUnsafeIteratorProbe then
    print("    __iterator probe skipped by default (pass --unsafe-iterator-probes to force; can crash MoonSharp VM in this runtime).")
else
    local customIteratorStatus, customIteratorResult = pcall(function()
        local chunk, loadErr = load([[local mt = {
    __iterator = function(self)
        local i = 0
        return function()
            i = i + 1
            if self[i] then
                return self[i]
            end
        end
    end
}
local t = setmetatable({100, 200, 300}, mt)
local out = {}
for v in t do
    table.insert(out, v)
end
return out]])
        if not chunk then
            error("custom iterator chunk load failed: " .. tostring(loadErr))
        end
        return chunk()
    end)
    if customIteratorStatus and type(customIteratorResult) == "table" then
        print("    __iterator values: " .. table.concat(customIteratorResult, ", "))
        if #customIteratorResult == 3 and customIteratorResult[1] == 100 and customIteratorResult[2] == 200 and customIteratorResult[3] == 300 then
            print("    __iterator works as expected.")
        else
            print("    __iterator returned unexpected values.")
        end
    else
        print("    __iterator unsupported/error: " .. tostring(customIteratorResult))
    end
end
