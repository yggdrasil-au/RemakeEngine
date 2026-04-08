import os
import re
from tree_sitter import Language, Parser
import tree_sitter_c_sharp as tscs

# --- Setup Tree-Sitter Parser ---
CSHARP_LANGUAGE = Language(tscs.language())
parser = Parser(CSHARP_LANGUAGE)

def clean_code(code):
    """Removes strings and comments to prevent false positive regex matches."""
    code = re.sub(r'"(?:\\.|[^"\\])*"', '""', code)
    code = re.sub(r'//.*', '', code)
    code = re.sub(r'/\*.*?\*/', '', code, flags=re.DOTALL)
    return code

def create_empty_stats():
    return {
        "Total Lines (Raw)": 0, "Code Lines": 0, "Comment Lines": 0, "Blank Lines": 0,
        "Flat Classes": 0, "Nested Classes": 0, 
        "Interfaces": 0, "Enums": 0, "Structs": 0, "Records": 0,
        "Functions (Total)": 0,
        "Func (Pub)": 0, "Func (Priv)": 0, "Func (Prot)": 0, "Func (Int)": 0,
        "Func (Pub Stat)": 0, "Func (Priv Stat)": 0, "Func (Prot Stat)": 0, "Func (Int Stat)": 0,
        "Variables (Total)": 0,
        "Vars (Public)": 0, "Vars (Private)": 0, "Vars (Protected)": 0, "Vars (Internal)": 0,
        "Vars (Static)": 0,
        "Max Nesting Level": 0,
        "Max Cyclomatic Complexity": 1,
        "Max Parameters": 0,
    }

def get_access_modifier(node):
    """Extracted from AST node modifiers."""
    modifiers = []
    for i in range(node.child_count):
        child = node.child(i)
        text = child.text.decode('utf8')
        if text in ["public", "private", "protected", "internal", "static", "readonly", "async", "virtual", "override"]:
            modifiers.append(text)
    
    access = "private"
    if "public" in modifiers: access = "public"
    elif "protected" in modifiers: 
        if "internal" in modifiers: access = "protected internal"
        else: access = "protected"
    elif "internal" in modifiers: access = "internal"
    elif "private" in modifiers: access = "private"
    
    return access, "static" in modifiers

def calculate_complexity(node):
    """Calculates cyclomatic complexity by walking decision points in a method."""
    complexity = 1
    decision_nodes = {
        'if_statement', 'for_statement', 'foreach_statement',
        'while_statement', 'do_statement', 'catch_clause', 
        'case_switch_label', 'conditional_expression', 'coalesce_expression'
    }
    
    # Boundary nodes prevent the complexity calculator from bleeding into nested lambdas/functions
    boundary_nodes = {
        'parenthesized_lambda_expression', 'lambda_expression', 
        'anonymous_method_expression', 'local_function_statement'
    }
    
    def walk(n, is_root=False):
        nonlocal complexity
        
        # Stop walking if we hit a nested function boundary (unless it's the root node we are evaluating)
        if not is_root and n.type in boundary_nodes:
            return

        if n.type in decision_nodes:
            complexity += 1
        elif n.type == 'binary_expression':
            operator = n.child_by_field_name('operator')
            if operator and operator.text.decode('utf8') in ['&&', '||']:
                complexity += 1
                
        for child in n.children:
            walk(child, is_root=False)
            
    walk(node, is_root=True)
    return complexity

def analyze_ast(node, inside_type=False, stats=None, current_depth=0):
    """Recursively walks the Abstract Syntax Tree to accurately map nested structures and members."""
    if stats is None:
        stats = create_empty_stats()

    is_type_decl = node.type in ['class_declaration', 'struct_declaration', 'record_declaration', 'interface_declaration', 'enum_declaration']

    # Track Deepest Nesting (Count code blocks)
    if node.type == 'block':
        current_depth += 1
        stats["Max Nesting Level"] = max(stats["Max Nesting Level"], current_depth)

    if node.type == 'class_declaration':
        if inside_type: stats["Nested Classes"] += 1
        else: stats["Flat Classes"] += 1
    elif node.type == 'interface_declaration': stats["Interfaces"] += 1
    elif node.type == 'enum_declaration': stats["Enums"] += 1
    elif node.type == 'struct_declaration': stats["Structs"] += 1
    elif node.type == 'record_declaration': stats["Records"] += 1
    
    # Handle Methods, Constructors, Local Functions, and Lambdas
    elif node.type in [
        'method_declaration', 'constructor_declaration', 'local_function_statement',
        'parenthesized_lambda_expression', 'lambda_expression', 'anonymous_method_expression'
    ]:
        
        # We only count standard methods/constructors toward the "Total Functions" tally to avoid skewing stats
        if node.type == 'method_declaration':
            access, is_static = get_access_modifier(node)
            stats["Functions (Total)"] += 1
            
            stat_access = access.split(' ')[0] if ' ' in access else access
            if stat_access not in ["public", "protected", "internal", "private"]:
                stat_access = "private"

            key_map = {
                "public": "Func (Pub Stat)" if is_static else "Func (Pub)",
                "protected": "Func (Prot Stat)" if is_static else "Func (Prot)",
                "internal": "Func (Int Stat)" if is_static else "Func (Int)",
                "private": "Func (Priv Stat)" if is_static else "Func (Priv)"
            }
            stats[key_map.get(stat_access, "Func (Priv)")] += 1

        # Analyze Parameters
        params_node = node.child_by_field_name('parameters')
        if params_node:
            param_count = sum(1 for c in params_node.children if c.type == 'parameter')
            stats["Max Parameters"] = max(stats["Max Parameters"], param_count)

        # Analyze Cyclomatic Complexity (Calculated independently for lambdas and parent functions)
        complexity = calculate_complexity(node)
        stats["Max Cyclomatic Complexity"] = max(stats["Max Cyclomatic Complexity"], complexity)

    # Handle Fields and Properties
    elif node.type in ['field_declaration', 'property_declaration']:
        access, is_static = get_access_modifier(node)
        stats["Variables (Total)"] += 1
        
        cap_access = (access.split(' ')[0] if ' ' in access else access).capitalize()
        key = f"Vars ({cap_access})"
        if key in stats: stats[key] += 1
        else: stats["Vars (Private)"] += 1

        if is_static: stats["Vars (Static)"] += 1

    for child in node.children:
        will_be_inside = inside_type or is_type_decl
        analyze_ast(child, will_be_inside, stats, current_depth)

    return stats

def count_line_types(lines):
    line_stats = {"Code": 0, "Comments": 0, "Blank": 0}
    in_block_comment = False
    
    for line in lines:
        stripped = line.strip()
        if not stripped:
            line_stats["Blank"] += 1
            continue
        
        if "/*" in stripped and "*/" in stripped:
            line_stats["Comments"] += 1
        elif "/*" in stripped:
            in_block_comment = True
            line_stats["Comments"] += 1
        elif "*/" in stripped:
            in_block_comment = False
            line_stats["Comments"] += 1
        elif in_block_comment:
            line_stats["Comments"] += 1
        elif stripped.startswith("//"):
            line_stats["Comments"] += 1
        else:
            line_stats["Code"] += 1
    return line_stats

def analyze_single_file(filepath):
    stats = create_empty_stats()
    try:
        with open(filepath, 'r', encoding='utf-8-sig') as f:
            raw_code = f.read()

        lines = raw_code.splitlines()
        stats["Total Lines (Raw)"] = len(lines)
        
        line_types = count_line_types(lines)
        stats["Code Lines"] = line_types["Code"]
        stats["Comment Lines"] = line_types["Comments"]
        stats["Blank Lines"] = line_types["Blank"]

        tree = parser.parse(bytes(raw_code, "utf8"))
        ast_stats = analyze_ast(tree.root_node)
        
        for key in ast_stats:
            if key in stats and key not in ["Total Lines (Raw)", "Code Lines", "Comment Lines", "Blank Lines"]:
                stats[key] = ast_stats[key]

    except Exception as e:
        print(f"Error reading {filepath}: {e}")

    return stats

def add_to_tree(tree_root, path_parts, file_stats):
    current = tree_root
    for part in path_parts[:-1]:
        if part not in current['children']:
            current['children'][part] = {'__type': 'dir', 'children': {}}
        current = current['children'][part]
    filename = path_parts[-1]
    current['children'][filename] = {'__type': 'file', 'stats': file_stats}

def build_project_tree(directory):
    tree = {'__type': 'dir', 'children': {}}
    global_stats = create_empty_stats()
    global_stats["Total .cs Files"] = 0
    
    max_keys = ["Max Nesting Level", "Max Cyclomatic Complexity", "Max Parameters"]

    for root, dirs, files in os.walk(directory):
        dirs[:] = [d for d in dirs if d.lower() not in ('bin', 'obj')]
        for file in files:
            if file.endswith(".cs") and not file.endswith(".Designer.cs"):
                filepath = os.path.join(root, file)
                rel_path = os.path.relpath(filepath, directory)
                path_parts = rel_path.split(os.sep)

                file_stats = analyze_single_file(filepath)
                add_to_tree(tree, path_parts, file_stats)

                global_stats["Total .cs Files"] += 1
                for key in file_stats:
                    if key in max_keys:
                        global_stats[key] = max(global_stats[key], file_stats[key])
                    else:
                        global_stats[key] += file_stats[key]

    return tree, global_stats

def print_tree_node(name, node, prefix="", is_last_dir=True, is_root=False):
    if is_root:
        print(f"\n📁 {name}")
        child_prefix = ""
    else:
        connector = "\\---" if is_last_dir else "+---"
        print(f"{prefix}{connector}{name}")
        child_prefix = prefix + ("    " if is_last_dir else "|   ")

    if node['__type'] == 'dir':
        children = node['children']
        files = [k for k, v in children.items() if v['__type'] == 'file']
        dirs = [k for k, v in children.items() if v['__type'] == 'dir']

        files.sort(key=str.lower)
        dirs.sort(key=str.lower)

        for f in files:
            file_node = children[f]
            print(f"{child_prefix}{f}")

            stats = file_node['stats']
            stat_indent = child_prefix + "    "

            print(f"{stat_indent}├─ Base:  L: {stats['Total Lines (Raw)']} ({stats['Code Lines']}C, {stats['Comment Lines']}#) | Cls: {stats['Flat Classes']} Flat, {stats['Nested Classes']} Nested | Int: {stats['Interfaces']} | Enum: {stats['Enums']}")
            print(f"{stat_indent}├─ Funcs: {stats['Functions (Total)']} (Pub: {stats['Func (Pub)']}+{stats['Func (Pub Stat)']}S, Priv: {stats['Func (Priv)']}+{stats['Func (Priv Stat)']}S, Prot: {stats['Func (Prot)']}+{stats['Func (Prot Stat)']}S, Int: {stats['Func (Int)']}+{stats['Func (Int Stat)']}S)")
            print(f"{stat_indent}├─ Vars:  {stats['Variables (Total)']} (Pub: {stats['Vars (Public)']}, Priv: {stats['Vars (Private)']}, Stat: {stats['Vars (Static)']})")
            print(f"{stat_indent}└─ Evals: Max Nesting: {stats['Max Nesting Level']} | Max Complexity: {stats['Max Cyclomatic Complexity']} | Max Params: {stats['Max Parameters']}")
            print(f"{child_prefix}")

        for i, d in enumerate(dirs):
            is_last_d = (i == len(dirs) - 1)
            print_tree_node(d, children[d], child_prefix, is_last_d, is_root=False)

def print_global_summary(stats):
    print("="*105)
    print(" 📊 GLOBAL PROJECT SUMMARY")
    print("="*105)
    print(f"Total .cs Files:    {stats['Total .cs Files']}")
    print(f"Total Lines:        {stats['Total Lines (Raw)']} ({stats['Code Lines']} Code, {stats['Comment Lines']} Comments, {stats['Blank Lines']} Blank)")
    print(f"Architecture:       {stats['Flat Classes']} Flat Classes, {stats['Nested Classes']} Nested Classes")
    print(f"                    {stats['Interfaces']} Interfaces, {stats['Enums']} Enums, {stats['Structs']} Structs, {stats['Records']} Records")
    
    pub_tot = stats['Func (Pub)'] + stats['Func (Pub Stat)']
    priv_tot = stats['Func (Priv)'] + stats['Func (Priv Stat)']
    prot_tot = stats['Func (Prot)'] + stats['Func (Prot Stat)']
    int_tot = stats['Func (Int)'] + stats['Func (Int Stat)']
    stat_tot = stats['Func (Pub Stat)'] + stats['Func (Priv Stat)'] + stats['Func (Prot Stat)'] + stats['Func (Int Stat)']

    print(f"Total Functions:    {stats['Functions (Total)']} (Stat: {stat_tot})")
    print(f"                    Public:    {pub_tot:<5} ({stats['Func (Pub)']} Inst, {stats['Func (Pub Stat)']} Stat)")
    print(f"                    Private:   {priv_tot:<5} ({stats['Func (Priv)']} Inst, {stats['Func (Priv Stat)']} Stat)")
    print(f"                    Protected: {prot_tot:<5} ({stats['Func (Prot)']} Inst, {stats['Func (Prot Stat)']} Stat)")
    print(f"                    Internal:  {int_tot:<5} ({stats['Func (Int)']} Inst, {stats['Func (Int Stat)']} Stat)")
    
    print(f"Total Variables:    {stats['Variables (Total)']} (Stat: {stats['Vars (Static)']})")
    print(f"                    Public:    {stats['Vars (Public)']}")
    print(f"                    Private:   {stats['Vars (Private)']}")
    print(f"                    Protected: {stats['Vars (Protected)']}")
    print(f"                    Internal:  {stats['Vars (Internal)']}")
    
    print("-" * 105)
    print(" 🚀 CODE QUALITY EVALUATIONS (Highest recorded values across project)")
    print("-" * 105)
    print(f"Deepest Nest Level:          {stats['Max Nesting Level']}")
    print(f"Max Cyclomatic Complexity:   {stats['Max Cyclomatic Complexity']}")
    print(f"Largest Number of Params:    {stats['Max Parameters']}")
    print("="*105 + "\n")

if __name__ == "__main__":
    target_directory = "./EngineNet"
    # target_directory = input("Enter the path to your C# project folder: ").strip()

    if os.path.isdir(target_directory):
        print("\nAnalyzing structure and metrics...")
        project_tree, global_stats = build_project_tree(target_directory)

        root_name = os.path.basename(os.path.abspath(target_directory))
        print_tree_node(root_name, project_tree, is_root=True)
        print_global_summary(global_stats)
    else:
        print("Invalid directory path. Please check the path and try again.")