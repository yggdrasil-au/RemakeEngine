-- Test script to verify linter warnings for disabled components in .luarc.json

-- These should ALL be flagged as "disabled" by the Lua Language Server

-- 0. Global scope stubs (basic)
LF = loadfile("some_file.lua")
dofile("some_file.lua")

-- 1. IO library - Standard IO & pipes
IOR = io.read("*a")
io.popen("ls")
io.flush()
io.input("input.txt")
io.output("output.txt")
IOT = io.tmpfile()
IOTT = io.type(handle)
io.lines("file.txt")

-- 2. OS library - System execution & restricted fs
os.execute("echo hello")
os.remove("file.txt")
os.rename("old.txt", "new.txt")
OST = os.tmpname()
os.setlocale("en_US")

-- PERSISTENT BUILTINS (These should NOT have warnings)
-- These are defined elsewhere in api_definitions.lua or keep their default behavior
print("This should be fine")
OST = os.time()
OSD = os.date()
IOO = io.open("test.txt", "w")

