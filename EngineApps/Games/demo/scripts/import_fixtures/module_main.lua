--[[
Deterministic fixture module for import and loader tests.
Top-level execution increments a global counter so tests can verify
that import/require re-execute the module (no caching).
--]]

---@class ImportFixtureModule
---@field Name string
---@field LoadCount integer
local Module = {}

local CounterKey = "__import_fixture_module_load_count"
local CurrentLoadCount = (_G[CounterKey] or 0) + 1
_G[CounterKey] = CurrentLoadCount

Module.Name = "ImportFixtureModule"
Module.LoadCount = CurrentLoadCount

---@return integer
function Module.GetLoadCount()
	return CurrentLoadCount
end

---@param left number
---@param right number
---@return number
function Module.Add(left, right)
	return left + right
end

---@param prefix string
---@return string
function Module.MakeLabel(prefix)
	return tostring(prefix) .. ":" .. tostring(CurrentLoadCount)
end

---@param left number
---@param right number
---@return number
---@return integer
function Module.GetNestedProduct(left, right)
	local nestedPath = join(Game_Root, "scripts", "import_fixtures", "nested", "module_nested.lua")
	---@type table
	local Nested = import(nestedPath)
	return Nested.Multiply(left, right), Nested.GetLoadCount()
end

return Module
