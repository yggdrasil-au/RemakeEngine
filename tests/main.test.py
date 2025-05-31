import unittest
from unittest.mock import patch, MagicMock, mock_open, call, ANY
from pathlib import Path # Keep this for creating REAL Path objects for MOCK_TOOL_ROOT
import json
import sys
import os

# Define constants from the module for easier use in tests
GAMES_REGISTRY_DIR_NAME = "RemakeRegistry"
GAMES_COLLECTION_DIR_NAME = "Games"
OPERATIONS_FILENAME = "operations.json"
ENGINE_CONFIG_FILENAME = "project.json"

TARGET_MODULE = "main"

# Ensure the main module and its utils can be imported
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'Utils')))

from printer import Colours
import main


@patch(f'{TARGET_MODULE}.Colours', Colours)
class TestDiscoverGames(unittest.TestCase):

    def setUp(self):
        self.patch_print = patch(f'{TARGET_MODULE}.print')
        self.mock_print = self.patch_print.start()

    def tearDown(self):
        self.patch_print.stop()
        patch.stopall()

    @patch(f'{TARGET_MODULE}.Path')
    def test_discover_games_no_collection_path(self, mock_path_constructor):
        mock_collection_path_instance = MagicMock(spec=Path)
        mock_collection_path_instance.is_dir.return_value = False

        games = main.discover_games(mock_collection_path_instance, OPERATIONS_FILENAME)
        self.assertEqual(games, {})
        self.mock_print.assert_called_with(Colours.YELLOW, f"Info: Games collection directory not found or is not a directory: {mock_collection_path_instance}")

    @patch(f'{TARGET_MODULE}.Path')
    def test_discover_games_empty_collection(self, mock_path_constructor):
        mock_collection_path_instance = MagicMock(spec=Path)
        mock_collection_path_instance.is_dir.return_value = True
        mock_collection_path_instance.iterdir.return_value = []

        games = main.discover_games(mock_collection_path_instance, OPERATIONS_FILENAME)
        self.assertEqual(games, {})

    @patch(f'{TARGET_MODULE}.json.load')
    @patch(f'{TARGET_MODULE}.open', new_callable=mock_open)
    def test_discover_games_valid_game(self, mock_file_open, mock_json_load):
        mock_game_dir = MagicMock(spec=Path)
        mock_game_dir.is_dir.return_value = True
        mock_game_dir.name = "Game1Dir"
        resolved_game_dir = Path("/abs/path/Game1Dir")
        mock_game_dir.resolve.return_value = resolved_game_dir

        mock_ops_file = MagicMock(spec=Path)
        mock_ops_file.is_file.return_value = True
        resolved_ops_file = resolved_game_dir / OPERATIONS_FILENAME
        mock_ops_file.resolve.return_value = resolved_ops_file

        mock_game_dir.__truediv__.return_value = mock_ops_file

        mock_collection_path = MagicMock(spec=Path)
        mock_collection_path.is_dir.return_value = True
        mock_collection_path.iterdir.return_value = [mock_game_dir]

        mock_json_load.return_value = {"Game1": [{"op": "details"}]}

        games = main.discover_games(mock_collection_path, OPERATIONS_FILENAME)

        self.assertIn("Game1", games)
        self.assertEqual(games["Game1"]["ops_file"], resolved_ops_file)
        self.assertEqual(games["Game1"]["game_root"], resolved_game_dir)
        mock_file_open.assert_called_once_with(mock_ops_file, 'r', encoding='utf-8')
        mock_json_load.assert_called_once()

    @patch(f'{TARGET_MODULE}.json.load')
    @patch(f'{TARGET_MODULE}.open', new_callable=mock_open)
    def test_discover_games_json_decode_error(self, mock_file_open, mock_json_load):
        mock_game_dir = MagicMock(spec=Path); mock_game_dir.is_dir.return_value = True
        mock_ops_file = MagicMock(spec=Path); mock_ops_file.is_file.return_value = True
        mock_game_dir.__truediv__.return_value = mock_ops_file
        mock_collection_path = MagicMock(spec=Path); mock_collection_path.is_dir.return_value = True
        mock_collection_path.iterdir.return_value = [mock_game_dir]

        mock_json_load.side_effect = json.JSONDecodeError("Error", "doc", 0)

        games = main.discover_games(mock_collection_path, OPERATIONS_FILENAME)
        self.assertEqual(games, {})
        self.mock_print.assert_any_call(Colours.RED, f"Error: Could not decode JSON from '{mock_ops_file}'. Ensure it's valid JSON.")

    @patch(f'{TARGET_MODULE}.json.load')
    @patch(f'{TARGET_MODULE}.open', new_callable=mock_open)
    def test_discover_games_invalid_ops_format_not_dict(self, mock_file_open, mock_json_load):
        mock_game_dir = MagicMock(spec=Path); mock_game_dir.is_dir.return_value = True
        mock_ops_file = MagicMock(spec=Path); mock_ops_file.is_file.return_value = True
        mock_game_dir.__truediv__.return_value = mock_ops_file
        mock_collection_path = MagicMock(spec=Path); mock_collection_path.is_dir.return_value = True
        mock_collection_path.iterdir.return_value = [mock_game_dir]

        mock_json_load.return_value = ["This is a list, not a dict"]

        games = main.discover_games(mock_collection_path, OPERATIONS_FILENAME)
        self.assertEqual(games, {})
        self.mock_print.assert_any_call(Colours.RED, f"Error: Operations file '{mock_ops_file}' should be a JSON object with a single top-level key (the game name), and its value should be a list of operations.")

    @patch(f'{TARGET_MODULE}.json.load')
    @patch(f'{TARGET_MODULE}.open', new_callable=mock_open)
    def test_discover_games_ops_value_not_list(self, mock_file_open, mock_json_load):
        mock_game_dir = MagicMock(spec=Path); mock_game_dir.is_dir.return_value = True
        mock_ops_file = MagicMock(spec=Path); mock_ops_file.is_file.return_value = True
        mock_game_dir.__truediv__.return_value = mock_ops_file
        mock_collection_path = MagicMock(spec=Path); mock_collection_path.is_dir.return_value = True
        mock_collection_path.iterdir.return_value = [mock_game_dir]

        mock_json_load.return_value = {"Game1": "This is a string, not a list of ops"}

        games = main.discover_games(mock_collection_path, OPERATIONS_FILENAME)
        self.assertEqual(games, {})
        self.mock_print.assert_any_call(Colours.RED, f"Error: Operations data for game 'Game1' in '{mock_ops_file}' is not a list.")

    @patch(f'{TARGET_MODULE}.json.load')
    @patch(f'{TARGET_MODULE}.open', new_callable=mock_open)
    def test_discover_games_duplicate_game_name_warning(self, mock_file_open, mock_json_load):
        mock_game1_dir = MagicMock(spec=Path); mock_game1_dir.is_dir.return_value = True; mock_game1_dir.name = "Game1Dir"
        mock_game1_ops_file = MagicMock(spec=Path); mock_game1_ops_file.is_file.return_value = True
        mock_game1_dir.__truediv__.return_value = mock_game1_ops_file
        resolved_game1_dir = Path("/abs/Game1Dir")
        resolved_game1_ops_file = resolved_game1_dir / "ops.json"
        mock_game1_dir.resolve.return_value = resolved_game1_dir
        mock_game1_ops_file.resolve.return_value = resolved_game1_ops_file

        mock_game2_dir = MagicMock(spec=Path); mock_game2_dir.is_dir.return_value = True; mock_game2_dir.name = "Game2Dir"
        mock_game2_ops_file = MagicMock(spec=Path); mock_game2_ops_file.is_file.return_value = True
        mock_game2_dir.__truediv__.return_value = mock_game2_ops_file
        resolved_game2_dir = Path("/abs/Game2Dir")
        resolved_game2_ops_file = resolved_game2_dir / "ops.json"
        mock_game2_dir.resolve.return_value = resolved_game2_dir
        mock_game2_ops_file.resolve.return_value = resolved_game2_ops_file

        mock_collection_path = MagicMock(spec=Path); mock_collection_path.is_dir.return_value = True
        mock_collection_path.iterdir.return_value = [mock_game1_dir, mock_game2_dir]

        mock_json_load.side_effect = [
            {"Game1": [{"op": "details1"}]},
            {"Game1": [{"op": "details2"}]}
        ]

        mock_file_open.side_effect = [
            mock_open(read_data=json.dumps({"Game1": [{"op": "details1"}]})).return_value,
            mock_open(read_data=json.dumps({"Game1": [{"op": "details2"}]})).return_value
        ]

        games = main.discover_games(mock_collection_path, "ops.json")

        self.assertIn("Game1", games)
        self.assertEqual(games["Game1"]["ops_file"], resolved_game2_ops_file)
        self.assertEqual(games["Game1"]["game_root"], resolved_game2_dir)

        self.mock_print.assert_any_call(Colours.YELLOW, f"Warning: Duplicate game name 'Game1' defined in '{mock_game2_ops_file}'. Overwriting previous entry from {resolved_game1_ops_file}.")
        self.assertEqual(mock_file_open.call_count, 2)
        self.assertEqual(mock_json_load.call_count, 2)


@patch(f'{TARGET_MODULE}.Colours', Colours)
class TestLoadOperations(unittest.TestCase):
    def setUp(self):
        self.patch_print = patch(f'{TARGET_MODULE}.print')
        self.mock_print = self.patch_print.start()

    def tearDown(self):
        self.patch_print.stop()
        patch.stopall()

    def test_load_operations_file_not_found(self):
        mock_file_path_instance = MagicMock(spec=Path)
        mock_file_path_instance.is_file.return_value = False

        ops = main.load_operations(mock_file_path_instance, "GameKey")
        self.assertEqual(ops, [])
        self.mock_print.assert_called_with(Colours.RED, f"Error: Operations file '{mock_file_path_instance}' not found or is not a file.")

    @patch(f'{TARGET_MODULE}.open', new_callable=mock_open)
    @patch(f'{TARGET_MODULE}.json.load')
    def test_load_operations_valid(self, mock_json_load, mock_file_open):
        mock_file_path = MagicMock(spec=Path)
        mock_file_path.is_file.return_value = True
        expected_ops = [{"name": "op1"}]
        mock_json_load.return_value = {"GameKey": expected_ops}

        ops = main.load_operations(mock_file_path, "GameKey")
        self.assertEqual(ops, expected_ops)
        mock_file_open.assert_called_once_with(mock_file_path, 'r', encoding='utf-8')

    @patch(f'{TARGET_MODULE}.open', new_callable=mock_open)
    @patch(f'{TARGET_MODULE}.json.load')
    def test_load_operations_key_not_found(self, mock_json_load, mock_file_open):
        mock_file_path = MagicMock(spec=Path)
        mock_file_path.is_file.return_value = True
        mock_json_load.return_value = {"AnotherKey": []}

        ops = main.load_operations(mock_file_path, "GameKey")
        self.assertEqual(ops, [])
        self.mock_print.assert_any_call(Colours.RED, f"Error: Did not find a list of operations under key 'GameKey' in '{mock_file_path}'. Found: <class 'NoneType'>")

    @patch(f'{TARGET_MODULE}.open', new_callable=mock_open)
    @patch(f'{TARGET_MODULE}.json.load')
    def test_load_operations_json_not_dict(self, mock_json_load, mock_file_open):
        mock_file_path = MagicMock(spec=Path); mock_file_path.is_file.return_value = True
        mock_json_load.return_value = ["not a dict"]

        ops = main.load_operations(mock_file_path, "GameKey")
        self.assertEqual(ops, [])
        self.mock_print.assert_any_call(Colours.RED, f"Error: Operations file '{mock_file_path}' is not structured as a dictionary with game name as key.")

    @patch(f'{TARGET_MODULE}.open', new_callable=mock_open)
    @patch(f'{TARGET_MODULE}.json.load')
    def test_load_operations_value_under_key_not_list(self, mock_json_load, mock_file_open):
        mock_file_path = MagicMock(spec=Path); mock_file_path.is_file.return_value = True
        mock_json_load.return_value = {"GameKey": "not a list"}

        ops = main.load_operations(mock_file_path, "GameKey")
        self.assertEqual(ops, [])
        self.mock_print.assert_any_call(Colours.RED, f"Error: Did not find a list of operations under key 'GameKey' in '{mock_file_path}'. Found: <class 'str'>")

    @patch(f'{TARGET_MODULE}.open', new_callable=mock_open)
    @patch(f'{TARGET_MODULE}.json.load')
    def test_load_operations_json_decode_error(self, mock_json_load, mock_file_open):
        mock_file_path = MagicMock(spec=Path); mock_file_path.is_file.return_value = True
        mock_json_load.side_effect = json.JSONDecodeError("msg", "doc", 0)

        ops = main.load_operations(mock_file_path, "GameKey")
        self.assertEqual(ops, [])
        self.mock_print.assert_any_call(Colours.RED, f"Error: Could not decode JSON from '{mock_file_path}'. Ensure it's valid JSON.")


@patch(f'{TARGET_MODULE}.Colours', Colours)
class TestResolvePlaceholders(unittest.TestCase):
    def setUp(self):
        self.patch_main_print = patch(f'{TARGET_MODULE}.print')
        self.mock_main_print = self.patch_main_print.start()

    def tearDown(self):
        self.patch_main_print.stop()
        patch.stopall()

    def test_resolve_simple_string(self):
        context = {"name": "World"}
        value = "Hello {{name}}!"
        expected = "Hello World!"
        result = main.resolve_placeholders(value, context)
        self.assertEqual(result, expected)
        self.mock_main_print.assert_any_call("Before resolving placeholders:", value)
        self.mock_main_print.assert_any_call("After resolving placeholders:", expected)

    def test_resolve_nested_path(self):
        context = {"user": {"name": "Alice", "id": 123}}
        value = "User: {{user.name}} ({{user.id}})"
        expected = "User: Alice (123)"
        result = main.resolve_placeholders(value, context)
        self.assertEqual(result, expected)
        self.mock_main_print.assert_any_call("Before resolving placeholders:", value)
        self.mock_main_print.assert_any_call("After resolving placeholders:", expected)

    def test_resolve_placeholder_not_found_keeps_placeholder(self):
        context = {"name": "World"}
        value = "Hello {{nonexistent}}!"
        expected = "Hello {{nonexistent}}!"
        result = main.resolve_placeholders(value, context)
        self.assertEqual(result, expected)
        self.mock_main_print.assert_any_call("Before resolving placeholders:", value)
        self.mock_main_print.assert_any_call("After resolving placeholders:", expected)

    def test_resolve_list_of_strings(self):
        context = {"item1": "Apple", "item2": "Banana"}
        value = ["Buy {{item1}}", "Get {{item2}}", "Orange"]
        expected = ["Buy Apple", "Get Banana", "Orange"]
        result = main.resolve_placeholders(value, context)
        self.assertEqual(result, expected)
        self.mock_main_print.assert_any_call("Before resolving placeholders:", value)
        self.mock_main_print.assert_any_call("After resolving placeholders (list):", expected)

    def test_resolve_dictionary_of_strings(self):
        context = {"fruit": "Mango", "color": "Yellow"}
        value = {"f": "{{fruit}}", "c": "{{color}} is its color."}
        expected = {"f": "Mango", "c": "Yellow is its color."}
        result = main.resolve_placeholders(value, context)
        self.assertEqual(result, expected)
        self.mock_main_print.assert_any_call("Before resolving placeholders:", value)
        self.mock_main_print.assert_any_call("After resolving placeholders (dict):", expected)

    def test_resolve_non_string_value_from_context_is_stringified(self):
        context = {"count": 10, "active": True}
        value_count = "Count: {{count}}"
        expected_count = "Count: 10"
        self.assertEqual(main.resolve_placeholders(value_count, context), expected_count)
        self.mock_main_print.assert_any_call("Before resolving placeholders:", value_count)
        self.mock_main_print.assert_any_call("After resolving placeholders:", expected_count)

        value_active = "Active: {{active}}"
        expected_active = "Active: True"
        self.assertEqual(main.resolve_placeholders(value_active, context), expected_active)
        self.mock_main_print.assert_any_call("Before resolving placeholders:", value_active)
        self.mock_main_print.assert_any_call("After resolving placeholders:", expected_active)

    def test_resolve_direct_mapping_if_present(self):
        context = {
            "regular": "value",
            "_direct_mapping_": {
                "DirectKey": "/path/to/something"
            }
        }
        value_direct = "Path is {{DirectKey}}"
        expected_direct = "Path is /path/to/something"
        self.assertEqual(main.resolve_placeholders(value_direct, context), expected_direct)
        self.mock_main_print.assert_any_call("Before resolving placeholders:", value_direct)
        self.mock_main_print.assert_any_call("After resolving placeholders:", expected_direct)

    def test_resolve_no_change_for_non_string_list_dict(self):
        context = {}
        value_int = 123
        self.assertEqual(main.resolve_placeholders(value_int, context), 123)
        self.mock_main_print.assert_any_call("Before resolving placeholders:", value_int)
        self.mock_main_print.assert_any_call("After resolving placeholders (unchanged):", value_int)

        value_bool = True
        self.assertTrue(main.resolve_placeholders(value_bool, context) is True)
        self.mock_main_print.assert_any_call("Before resolving placeholders:", value_bool)
        self.mock_main_print.assert_any_call("After resolving placeholders (unchanged):", value_bool)


@patch(f'{TARGET_MODULE}.Colours', Colours)
class TestCheckScriptsExist(unittest.TestCase):
    def setUp(self):
        self.patch_print = patch(f'{TARGET_MODULE}.print')
        self.mock_print = self.patch_print.start()

    def tearDown(self):
        self.patch_print.stop()
        patch.stopall()

    @patch(f'{TARGET_MODULE}.Path')
    def test_all_scripts_exist(self, mock_path_class):
        operations = [
            {"Name": "Op1", "script": "script1.py"},
            {"Name": "Op2", "script": "script2.py"}
        ]

        mock_script1_path = MagicMock(spec=Path)
        mock_script1_path.is_file.return_value = True
        mock_script2_path = MagicMock(spec=Path)
        mock_script2_path.is_file.return_value = True

        mock_path_class.side_effect = lambda x: mock_script1_path if x == "script1.py" else mock_script2_path

        result = main.check_scripts_exist(operations, Path("ops.json"))
        self.assertTrue(result)
        self.assertEqual(mock_path_class.call_count, 2)

    @patch(f'{TARGET_MODULE}.Path')
    def test_script_missing(self, mock_path_class):
        operations = [{"Name": "Op1", "script": "missing_script.py"}]

        mock_missing_script_path_obj = MagicMock(spec=Path)
        mock_missing_script_path_obj.is_file.return_value = False
        mock_path_class.return_value = mock_missing_script_path_obj

        log_path = Path("ops.json")
        result = main.check_scripts_exist(operations, log_path)

        self.assertFalse(result)
        self.mock_print.assert_any_call(Colours.RED, "Error: Script for operation 'Op1' not found at: missing_script.py")
        self.mock_print.assert_any_call(Colours.YELLOW, f"       (Defined in '{log_path}', script base path: 'missing_script.py')")

    def test_no_script_key_or_empty_script(self):
        operations = [
            {"Name": "Op1"},
            {"Name": "Op2", "script": ""}
        ]
        result = main.check_scripts_exist(operations, Path("ops.json"))
        self.assertTrue(result)

    def test_empty_operations_list(self):
        result = main.check_scripts_exist([], Path("ops.json"))
        self.assertTrue(result)

    @patch(f'{TARGET_MODULE}.Path')
    def test_unnamed_operation_script_missing(self, mock_path_class):
        operations = [{"script": "missing_script.py"}]

        mock_missing_script_path_obj = MagicMock(spec=Path)
        mock_missing_script_path_obj.is_file.return_value = False
        mock_path_class.return_value = mock_missing_script_path_obj

        log_path = Path("ops.json")
        result = main.check_scripts_exist(operations, log_path)
        self.assertFalse(result)
        self.mock_print.assert_any_call(Colours.RED, "Error: Script for operation 'Unnamed Operation #1' not found at: missing_script.py")


@patch(f'{TARGET_MODULE}.Colours', Colours)
class TestMainToolLogicFlow(unittest.TestCase):

    def setUp(self):
        self.patch_cwd = patch(f'{TARGET_MODULE}.Path.cwd')
        self.mock_cwd = self.patch_cwd.start()
        # self.MOCK_TOOL_ROOT is a real pathlib.Path object.
        # This is crucial for Path.cwd().return_value to behave like a real path for / ops.
        self.MOCK_TOOL_ROOT = Path("/fake/tool/root")
        self.mock_cwd.return_value = self.MOCK_TOOL_ROOT

        self.patch_discover_games = patch(f'{TARGET_MODULE}.discover_games')
        self.mock_discover_games = self.patch_discover_games.start()

        self.patch_load_operations = patch(f'{TARGET_MODULE}.load_operations')
        self.mock_load_operations = self.patch_load_operations.start()

        self.patch_check_scripts_exist = patch(f'{TARGET_MODULE}.check_scripts_exist')
        self.mock_check_scripts_exist = self.patch_check_scripts_exist.start()
        self.mock_check_scripts_exist.return_value = True

        self.patch_resolve_placeholders = patch(f'{TARGET_MODULE}.resolve_placeholders', side_effect=lambda val, ctx: val)
        self.mock_resolve_placeholders = self.patch_resolve_placeholders.start()

        self.patch_q_select = patch(f'{TARGET_MODULE}.questionary.select')
        self.mock_q_select = self.patch_q_select.start()
        self.patch_q_confirm = patch(f'{TARGET_MODULE}.questionary.confirm')
        self.mock_q_confirm = self.patch_q_confirm.start()
        self.patch_q_text = patch(f'{TARGET_MODULE}.questionary.text')
        self.mock_q_text = self.patch_q_text.start()
        self.patch_q_checkbox = patch(f'{TARGET_MODULE}.questionary.checkbox')
        self.mock_q_checkbox = self.patch_q_checkbox.start()

        self.mock_q_select.return_value.ask.return_value = "Exit Tool"
        self.mock_q_confirm.return_value.ask.return_value = False
        self.mock_q_text.return_value.ask.return_value = ""
        self.mock_q_checkbox.return_value.ask.return_value = []

        self.patch_subprocess_popen = patch(f'{TARGET_MODULE}.subprocess.Popen')
        self.mock_subprocess_popen = self.patch_subprocess_popen.start()
        self.mock_process = MagicMock()
        self.mock_process.wait.return_value = None
        self.mock_process.returncode = 0
        self.mock_subprocess_popen.return_value = self.mock_process

        self.patch_open = patch(f'{TARGET_MODULE}.open', new_callable=mock_open)
        self.mock_open_file = self.patch_open.start()

        self.patch_json_load = patch(f'{TARGET_MODULE}.json.load')
        self.mock_json_load = self.patch_json_load.start()
        self.MOCK_ENGINE_CONFIG = {"engine_key": "engine_value"}
        self.mock_json_load.return_value = self.MOCK_ENGINE_CONFIG

        self.patch_print = patch(f'{TARGET_MODULE}.print')
        self.mock_print = self.patch_print.start()

        self.patch_input = patch('builtins.input')
        self.mock_input = self.patch_input.start()
        self.mock_input.return_value = ""

        self.patch_os_system = patch(f'{TARGET_MODULE}.os.system')
        self.mock_os_system = self.patch_os_system.start()

        # This patches main.Path (the class)
        self.patch_path_class = patch(f'{TARGET_MODULE}.Path', spec=Path) # Use spec=Path
        self.mock_path_class = self.patch_path_class.start()

        # This mock is for when main.py does Path(str_arg)
        # It needs to behave like a Path object, esp. for __str__ and is_file/is_dir
        self.engine_config_path_str = str(self.MOCK_TOOL_ROOT / ENGINE_CONFIG_FILENAME)
        self.mock_engine_config_path_obj = MagicMock(spec=Path, name="EngineConfigPathMock")
        self.mock_engine_config_path_obj.is_file.return_value = True
        self.mock_engine_config_path_obj.__str__ = lambda s: self.engine_config_path_str # Returns the string path

        self.specific_path_to_mock_obj = {}

        def general_path_factory(path_input_str):
            # This mock is returned when main.py calls Path(some_string)
            # e.g. script_abs_path = Path(script_rel_path_str)
            mock_p = MagicMock(spec=Path, name=f"MockedPathInstance({path_input_str})")
            mock_p.is_file.return_value = False # Default
            mock_p.is_dir.return_value = False  # Default
            # CRITICAL: When str(Path(script_rel_path_str)) is called in main.py,
            # it should return the script_rel_path_str itself.
            mock_p.__str__ = lambda s: path_input_str
            mock_p.name = Path(path_input_str).name # Delegate to real Path for these
            mock_p.parent = Path(path_input_str).parent

            # Mock resolve() to return a mock that also stringifies to a "resolved" path string
            if Path(path_input_str).is_absolute():
                resolved_str = path_input_str
            else:
                # Assume MOCK_TOOL_ROOT is the CWD for resolving relative paths in this context
                resolved_str = str(self.MOCK_TOOL_ROOT / path_input_str)

            resolved_path_obj_for_mock = MagicMock(spec=Path, name=f"Resolved({resolved_str})")
            resolved_path_obj_for_mock.__str__ = lambda s: resolved_str
            resolved_path_obj_for_mock.is_file.return_value = mock_p.is_file.return_value # Inherit
            mock_p.resolve.return_value = resolved_path_obj_for_mock

            # If main.py does Path(x) / "y", this __truediv__ should be on the mock_p
            mock_p.__truediv__ = lambda s, other: self.mock_path_class(Path(str(s)) / str(other))

            return mock_p

        def path_side_effect_router(path_arg):
            # path_arg is what's passed to Path() in main.py
            # Convert to string for dictionary key and factory use
            path_str_key = str(path_arg)

            if path_str_key == self.engine_config_path_str:
                return self.mock_engine_config_path_obj
            if path_str_key in self.specific_path_to_mock_obj:
                return self.specific_path_to_mock_obj[path_str_key]

            # If not a specific pre-configured mock, use the general factory
            return general_path_factory(path_str_key)

        self.mock_path_class.side_effect = path_side_effect_router


    def tearDown(self):
        patch.stopall()

    def _setup_script_path_mock(self, script_path_in_json_str, is_file=True):
        # This helper configures what self.mock_path_class (i.e., main.Path)
        # will return when called with script_path_in_json_str.
        mock_script_obj = MagicMock(spec=Path, name=f"ScriptPathMock_{script_path_in_json_str}")
        mock_script_obj.is_file.return_value = is_file
        # When main.py does str(Path(script_path_in_json_str)), this __str__ is called.
        mock_script_obj.__str__ = lambda s: script_path_in_json_str

        # Configure resolve behavior for this specific mock
        if Path(script_path_in_json_str).is_absolute():
            resolved_path_str = script_path_in_json_str
        else:
            resolved_path_str = str(self.MOCK_TOOL_ROOT / script_path_in_json_str)

        resolved_mock = MagicMock(spec=Path, name=f"Resolved_{resolved_path_str}")
        resolved_mock.__str__ = lambda s: resolved_path_str
        resolved_mock.is_file.return_value = is_file # Resolve keeps file status for this mock
        mock_script_obj.resolve.return_value = resolved_mock

        self.specific_path_to_mock_obj[script_path_in_json_str] = mock_script_obj
        return mock_script_obj


    #def test_no_games_found_triggers_exit_message(self):
    #    self.mock_discover_games.return_value = {}
    #    self.mock_engine_config_path_obj.is_file.return_value = False

    #    result = main.main_tool_logic()
    #    self.assertFalse(result)

    #    # games_registry_full_path in main.py is constructed from Path.cwd().return_value
    #    # which is self.MOCK_TOOL_ROOT (a real Path object).
    #    # So, str(games_registry_full_path) in main.py will use the real Path's __str__.
    #    path_as_in_main_py = self.MOCK_TOOL_ROOT / GAMES_REGISTRY_DIR_NAME / GAMES_COLLECTION_DIR_NAME
    #    print("\nPath as in main.py:", str(path_as_in_main_py))  # For debugging
    #    expected_message_arg = f"No valid game configurations found in subdirectories of '{str(path_as_in_main_py)}'."
    #    print("Expected message arg:", expected_message_arg)  # For debugging

    #    print("Actual mock_print calls:", self.mock_print.mock_calls) # For debugging

    #    self.mock_print.assert_any_call(Colours.RED, expected_message_arg)
    #    self.mock_input.assert_called_with("Press Enter to close.")


    def test_game_selection_and_exit_tool(self):
        # ops_file and game_root can be simple Path objects for discover_games mock return
        self.mock_discover_games.return_value = {
            "Game1": {"ops_file": Path("/fake/ops1.json"), "game_root": Path("/fake/game1root")}
        }
        self.mock_q_select.return_value.ask.return_value = "Exit Tool"

        result = main.main_tool_logic()
        self.assertFalse(result)
        self.mock_print.assert_any_call(Colours.CYAN, "Exiting tool...")

    def test_game_selected_no_operations_found(self):
        game_root_path = Path("/fake/game1root") # Real Path for game_root context

        # operations_file_for_game in main.py will be what discover_games returns.
        # Let's make it a mock that stringifies to a clean path.
        ops_file_path_str_for_mock = "/fake/game1root/operations.json"
        mock_ops_file_obj_from_discover = MagicMock(spec=Path)
        mock_ops_file_obj_from_discover.__str__ = lambda s: ops_file_path_str_for_mock
        # This mock_ops_file_obj_from_discover is what load_operations will receive.
        # Its is_file will be checked by load_operations.
        mock_ops_file_obj_from_discover.is_file.return_value = True # Assume it exists for load_operations

        self.mock_discover_games.return_value = {
            "Game1": {"ops_file": mock_ops_file_obj_from_discover, "game_root": game_root_path}
        }
        self.mock_q_select.return_value.ask.return_value = "Game1"
        self.mock_load_operations.return_value = [] # No operations loaded

        result = main.main_tool_logic()
        self.assertTrue(result)
        # The path printed is str(operations_file_for_game)
        self.mock_print.assert_any_call(Colours.RED, f"No valid operations found for 'Game1'. Check content of '{ops_file_path_str_for_mock}' under the key 'Game1'.")
        self.mock_input.assert_called_with("Press Enter to return to game selection.")

    def test_check_scripts_exist_fails_returns_to_game_selection(self):
        game_root_path = Path("/fake/game1root")
        ops_file_path_str_for_mock = "/ops/file.json"
        mock_ops_file_obj_from_discover = MagicMock(spec=Path)
        mock_ops_file_obj_from_discover.__str__ = lambda s: ops_file_path_str_for_mock
        mock_ops_file_obj_from_discover.is_file.return_value = True


        self.mock_discover_games.return_value = {
            "Game1": {"ops_file": mock_ops_file_obj_from_discover, "game_root": game_root_path}
        }
        self.mock_q_select.return_value.ask.return_value = "Game1"
        self.mock_load_operations.return_value = [{"Name": "OpWithMissingScript", "script": "miss.py"}]
        self.mock_check_scripts_exist.return_value = False # This mock triggers the path

        result = main.main_tool_logic()

        self.assertTrue(result)
        self.mock_print.assert_any_call(Colours.RED, f"One or more essential scripts for 'Game1' are missing or misconfigured.")
        self.mock_input.assert_called_with("Press Enter to return to game selection.")

    def test_init_script_success_then_exit_from_op_menu(self):
        game_name = "TestGame"
        game_root = self.MOCK_TOOL_ROOT / "games" / game_name
        ops_file_str = str(game_root / OPERATIONS_FILENAME)
        mock_ops_file_obj = self._setup_script_path_mock(ops_file_str, is_file=True) # For load_operations

        init_script_name_in_json = "init.py" # This is what's in operations.json
        # This setup ensures that when main.py does Path("init.py"), it gets a mock
        # whose is_file() is True and __str__() returns "init.py".
        self._setup_script_path_mock(init_script_name_in_json, is_file=True)

        self.mock_discover_games.return_value = {
            game_name: {"ops_file": mock_ops_file_obj, "game_root": game_root}
        }
        self.mock_q_select.return_value.ask.side_effect = [game_name, "Exit Tool"]

        init_op = {"Name": "AutoInit", "script": init_script_name_in_json, "init": True, "args": ["--initarg"]}
        self.mock_load_operations.return_value = [init_op]
        self.mock_check_scripts_exist.return_value = True
        self.mock_process.returncode = 0

        result = main.main_tool_logic()

        self.assertFalse(result)
        # main.py calls Popen with str(Path(init_script_name_in_json)), which is init_script_name_in_json
        self.mock_subprocess_popen.assert_called_once_with(
            ["python", init_script_name_in_json, "--initarg"],
        )
        self.mock_print.assert_any_call(Colours.GREEN, f"\nInitialization script 'AutoInit' completed successfully.")
        self.mock_input.assert_any_call() # After init script success

    def test_init_script_execution_fails_returns_to_game_selection(self):
        game_name = "FailInitGame"
        game_root = self.MOCK_TOOL_ROOT / "games" / game_name
        ops_file_str = str(game_root / OPERATIONS_FILENAME)
        mock_ops_file_obj = self._setup_script_path_mock(ops_file_str, is_file=True)

        init_script_name_in_json = "failing_init.py"
        self._setup_script_path_mock(init_script_name_in_json, is_file=True)

        self.mock_discover_games.return_value = {
            game_name: {"ops_file": mock_ops_file_obj, "game_root": game_root}
        }
        self.mock_q_select.return_value.ask.return_value = game_name

        init_op = {"Name": "MyFailingInit", "script": init_script_name_in_json, "init": True}
        self.mock_load_operations.return_value = [init_op]
        self.mock_check_scripts_exist.return_value = True
        self.mock_process.returncode = 1

        result = main.main_tool_logic()

        self.assertTrue(result)
        self.mock_subprocess_popen.assert_called_once_with(["python", init_script_name_in_json])
        self.mock_print.assert_any_call(Colours.RED, f"\nInitialization script 'MyFailingInit' failed with exit code 1.")
        self.mock_print.assert_any_call(Colours.RED, f"Due to initialization failure for '{game_name}', returning to game selection.")
        self.mock_input.assert_any_call("Press Enter to continue.")

    def test_operation_with_confirm_prompt_yes_adds_cli_arg(self):
        game_name = "PromptGame"
        game_root = self.MOCK_TOOL_ROOT / "games" / game_name
        ops_file_str = str(game_root / OPERATIONS_FILENAME)
        mock_ops_file_obj = self._setup_script_path_mock(ops_file_str, is_file=True)

        op_script_name_in_json = "op_script.py"
        self._setup_script_path_mock(op_script_name_in_json, is_file=True)

        self.mock_discover_games.return_value = {
            game_name: {"ops_file": mock_ops_file_obj, "game_root": game_root}
        }
        self.mock_q_select.return_value.ask.side_effect = [game_name, "ConfirmOp", "Exit Tool"]

        prompt_config = {
            "Name": "ConfirmAction", "type": "confirm",
            "message": "Proceed?", "cli_arg": "--proceed"
        }
        op_config = {"Name": "ConfirmOp", "script": op_script_name_in_json, "prompts": [prompt_config]}
        self.mock_load_operations.return_value = [op_config]
        self.mock_check_scripts_exist.return_value = True
        self.mock_q_confirm.return_value.ask.return_value = True
        self.mock_process.returncode = 0

        main.main_tool_logic()

        self.mock_q_confirm.assert_called_once_with("Proceed?", default=False, style=ANY)
        self.mock_subprocess_popen.assert_called_with(
            ["python", op_script_name_in_json, "--proceed"]
        )
        self.mock_print.assert_any_call(Colours.GREEN, f"\nOperation 'ConfirmOp' completed successfully.")

    def test_operation_with_text_prompt_uses_template(self):
        game_name = "TextPromptGame"
        op_name = "TextOp"
        ops_file_str = "ops_text_prompt.json"
        game_root_real_path = Path("root_text_prompt") # For discover_games return
        mock_ops_file_obj = self._setup_script_path_mock(ops_file_str, is_file=True)

        op_script_name_in_json = "text_op_script.py"
        self._setup_script_path_mock(op_script_name_in_json, is_file=True)

        self.mock_discover_games.return_value = {
            game_name: {"ops_file": mock_ops_file_obj, "game_root": game_root_real_path}
        }
        self.mock_q_select.return_value.ask.side_effect = [game_name, op_name, "Exit Tool"]

        prompt_config = {
            "Name": "InputName", "type": "text", "message": "Enter name:",
            "cli_arg_template": "--name={value}"
        }
        op_config = {"Name": op_name, "script": op_script_name_in_json, "prompts": [prompt_config]}
        self.mock_load_operations.return_value = [op_config]
        self.mock_q_text.return_value.ask.return_value = "Alice"

        main.main_tool_logic()

        self.mock_q_text.assert_called_once_with("Enter name:", default="", style=ANY, validate=ANY)
        self.mock_subprocess_popen.assert_called_with(
            ["python", op_script_name_in_json, "--name=Alice"]
        )

    def test_operation_prompt_cancellation_skips_op(self):
        game_name = "CancelGame"
        op_name = "CancellableOp"
        ops_file_str = "ops_cancel.json"
        game_root_real_path = Path("root_cancel")
        mock_ops_file_obj = self._setup_script_path_mock(ops_file_str, is_file=True)

        op_script_name_in_json = "script.py"
        self._setup_script_path_mock(op_script_name_in_json, is_file=True)


        self.mock_discover_games.return_value = {
            game_name: {"ops_file": mock_ops_file_obj, "game_root": game_root_real_path}
        }
        self.mock_q_select.return_value.ask.side_effect = [game_name, op_name, "Exit Tool"]

        prompt_config = {"Name": "ConfirmX", "type": "confirm", "message": "Sure?"}
        op_config = {"Name": op_name, "script": op_script_name_in_json, "prompts": [prompt_config]}
        self.mock_load_operations.return_value = [op_config]

        self.mock_q_confirm.return_value.ask.return_value = None

        main.main_tool_logic()

        self.mock_subprocess_popen.assert_not_called()
        self.mock_print.assert_any_call(Colours.RED, f"Configuration for '{op_name}' cancelled by user. Skipping operation.")
        self.mock_input.assert_any_call("\nPress Enter to return to the menu.")


if __name__ == '__main__':
    unittest.main(argv=['first-arg-is-ignored'], exit=False)