--[[
Nested fixture module imported by module_main.lua using a relative path.
This validates script_dir-aware nested imports.
--]]

---@class ImportFixtureNestedModule
---@field Name string
---@field LoadCount integer
local Module = {}

local CounterKey = "__import_fixture_nested_load_count"
local CurrentLoadCount = (_G[CounterKey] or 0) + 1
_G[CounterKey] = CurrentLoadCount

Module.Name = "ImportFixtureNestedModule"
Module.LoadCount = CurrentLoadCount

---@return integer
function Module.GetLoadCount()
	return CurrentLoadCount
end

---@param left number
---@param right number
---@return number
function Module.Multiply(left, right)
	return left * right
end

---@return string
function Module.Describe()
	return "Nested:" .. tostring(CurrentLoadCount)
end

return Module
