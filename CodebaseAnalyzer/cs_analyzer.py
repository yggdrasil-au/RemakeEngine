import sys
sys.stdout.reconfigure(encoding='utf-8')

import os
import re
import textwrap
from collections import defaultdict
from tree_sitter import Language, Parser
import tree_sitter_c_sharp as tscs

# --- Setup Tree-Sitter Parser ---
CSHARP_LANGUAGE = Language(tscs.language())
parser = Parser(CSHARP_LANGUAGE)

# --- Directories to Ignore ---
IGNORE_DIRS = {
    'bin', 'obj', '.git', '.idea', '.vscode', '.history',
    'engineapps', 'remakeenginedocs', 'enginebuild',
    'schemas', '.bettergit', '.github', 'enginenettest'
}

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
        "Total Complexity": 0, # NEW: Added to calculate averages
        "Max Parameters": 0,
        "ClassDetails": [],
    }

def get_access_modifier(node):
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
    complexity = 1
    decision_nodes = {
        'if_statement', 'for_statement', 'foreach_statement',
        'while_statement', 'do_statement', 'catch_clause',
        'case_switch_label', 'conditional_expression', 'coalesce_expression'
    }
    boundary_nodes = {
        'parenthesized_lambda_expression', 'lambda_expression',
        'anonymous_method_expression', 'local_function_statement'
    }

    def walk(n, is_root=False):
        nonlocal complexity
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

def analyze_ast(node, inside_type=False, stats=None, current_depth=0, current_namespace="Global", current_class_path="", global_class_registry=None):
    if stats is None:
        stats = create_empty_stats()

    is_type_decl = node.type in ['class_declaration', 'struct_declaration', 'record_declaration', 'interface_declaration', 'enum_declaration']

    next_namespace = current_namespace
    next_class_path = current_class_path

    if node.type == 'compilation_unit':
        for child in node.children:
            if child.type == 'file_scoped_namespace_declaration':
                name_node = child.child_by_field_name('name')
                if name_node:
                    next_namespace = name_node.text.decode('utf8')
                break

    if node.type == 'namespace_declaration':
        name_node = node.child_by_field_name('name')
        if name_node:
            next_namespace = name_node.text.decode('utf8')

    if node.type == 'block':
        current_depth += 1
        stats["Max Nesting Level"] = max(stats["Max Nesting Level"], current_depth)

    if node.type == 'class_declaration':
        name_node = node.child_by_field_name('name')
        cls_name = name_node.text.decode('utf8') if name_node else "UnknownClass"

        if current_class_path:
            next_class_path = f"{current_class_path}.{cls_name}"
        else:
            next_class_path = f"{next_namespace}.{cls_name}"

        class_loc = node.end_point[0] - node.start_point[0] + 1
        stats["ClassDetails"].append((cls_name, class_loc))

        if global_class_registry is not None:
            global_class_registry[next_class_path] += class_loc

        if inside_type: stats["Nested Classes"] += 1
        else: stats["Flat Classes"] += 1

    elif node.type == 'interface_declaration': stats["Interfaces"] += 1
    elif node.type == 'enum_declaration': stats["Enums"] += 1
    elif node.type == 'struct_declaration': stats["Structs"] += 1
    elif node.type == 'record_declaration': stats["Records"] += 1

    elif node.type in [
        'method_declaration', 'constructor_declaration', 'local_function_statement',
        'parenthesized_lambda_expression', 'lambda_expression', 'anonymous_method_expression'
    ]:
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

        params_node = node.child_by_field_name('parameters')
        if params_node:
            param_count = sum(1 for c in params_node.children if c.type == 'parameter')
            stats["Max Parameters"] = max(stats["Max Parameters"], param_count)

        complexity = calculate_complexity(node)
        stats["Max Cyclomatic Complexity"] = max(stats["Max Cyclomatic Complexity"], complexity)
        stats["Total Complexity"] += complexity # NEW: Tally for average calculations

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
        analyze_ast(child, will_be_inside, stats, current_depth, next_namespace, next_class_path, global_class_registry)

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

def analyze_single_file(filepath, global_class_registry=None):
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
        ast_stats = analyze_ast(tree.root_node, global_class_registry=global_class_registry)

        for key in ast_stats:
            if key in stats and key not in ["Total Lines (Raw)", "Code Lines", "Comment Lines", "Blank Lines", "ClassDetails"]:
                stats[key] = ast_stats[key]

        stats["ClassDetails"] = ast_stats.get("ClassDetails", [])

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

def discover_projects(root_directory):
    csproj_files = {}
    proj_ref_pattern = re.compile(r'<ProjectReference\s+Include\s*=\s*"([^"]+)"', re.IGNORECASE)

    for root, dirs, files in os.walk(root_directory):
        dirs[:] = [d for d in dirs if d.lower() not in IGNORE_DIRS]

        for f in files:
            if f.endswith('.csproj'):
                abs_path = os.path.abspath(os.path.join(root, f))
                csproj_files[abs_path] = {
                    'name': f[:-7],
                    'path': abs_path,
                    'dir': os.path.abspath(root),
                    'refs': [],
                    'loc': 0,
                    'files': 0
                }

    for p_path, p_data in csproj_files.items():
        try:
            with open(p_path, 'r', encoding='utf-8-sig') as f:
                content = f.read()
            refs = proj_ref_pattern.findall(content)

            for ref in refs:
                ref_path = os.path.normpath(os.path.join(p_data['dir'], ref.replace('\\', os.sep)))
                if ref_path in csproj_files:
                    p_data['refs'].append(csproj_files[ref_path]['name'])
                else:
                    p_data['refs'].append(os.path.basename(ref_path)[:-7] + " (External)")
        except Exception:
            pass

    sorted_projs = sorted(csproj_files.values(), key=lambda x: len(x['dir']), reverse=True)
    return csproj_files, sorted_projs

def get_owning_project(filepath, sorted_projs):
    abs_filepath = os.path.abspath(filepath)
    for p in sorted_projs:
        if abs_filepath.startswith(p['dir'] + os.sep) or abs_filepath == p['dir']:
            return p
    return None

def build_project_tree(directory):
    tree = {'__type': 'dir', 'children': {}}
    global_stats = create_empty_stats()
    global_stats["Total .cs Files"] = 0
    alerts = [] # NEW: Collect actionable warnings

    global_class_registry = defaultdict(int)
    file_class_counts = {}

    max_keys = ["Max Nesting Level", "Max Cyclomatic Complexity", "Max Parameters"]

    projects_dict, sorted_projs = discover_projects(directory)

    for root, dirs, files in os.walk(directory):
        dirs[:] = [d for d in dirs if d.lower() not in IGNORE_DIRS]

        for file in files:
            if file.endswith(".cs") and not file.endswith(".Designer.cs"):
                filepath = os.path.join(root, file)
                rel_path = os.path.relpath(filepath, directory)
                path_parts = rel_path.split(os.sep)

                file_stats = analyze_single_file(filepath, global_class_registry)
                add_to_tree(tree, path_parts, file_stats)

                # NEW: Generate Alerts
                if file_stats["Max Cyclomatic Complexity"] > 15:
                    alerts.append(f"[COMPLEXITY] {file} (Max: {file_stats['Max Cyclomatic Complexity']})")
                if file_stats["Max Nesting Level"] > 5:
                    alerts.append(f"[DEEP NESTING] {file} (Max Depth: {file_stats['Max Nesting Level']})")
                if file_stats["Total Lines (Raw)"] > 600:
                    alerts.append(f"[GOD FILE] {file} (Lines: {file_stats['Total Lines (Raw)']})")

                owner = get_owning_project(filepath, sorted_projs)
                if owner:
                    owner['loc'] += file_stats["Total Lines (Raw)"]
                    owner['files'] += 1

                file_class_counts[filepath] = {
                    "classes": file_stats["Flat Classes"] + file_stats["Nested Classes"],
                    "loc": file_stats["Total Lines (Raw)"],
                    "details": file_stats["ClassDetails"]
                }

                global_stats["Total .cs Files"] += 1
                for key in file_stats:
                    if key in max_keys:
                        global_stats[key] = max(global_stats[key], file_stats[key])
                    elif key != "ClassDetails":
                        global_stats[key] += file_stats[key]

    return tree, global_stats, global_class_registry, file_class_counts, projects_dict, alerts

def print_project_graph(projects_dict):
    print("="*105)
    print(" 📦 SUBPROJECT ARCHITECTURE & DEPENDENCY GRAPH")
    print("="*105)

    if not projects_dict:
        print("No .csproj files found.")
        return

    sorted_projects = sorted(projects_dict.values(), key=lambda x: x['name'].lower())

    for p in sorted_projects:
        print(f" {p['name']} ({p['files']} files, {p['loc']} lines)")
        if p['refs']:
            for i, ref in enumerate(p['refs']):
                connector = "└─" if i == len(p['refs']) - 1 else "├─"
                print(f"   {connector} Depends on: {ref}")
        else:
            print("   └─ No internal project references.")
        print("")

def print_tree_node(name, node, prefix="", is_last_dir=True, is_root=False):
    if is_root:
        print(f"📁 {name}")
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

            # NEW: Calculate Average Complexity
            avg_comp = (stats['Total Complexity'] / stats['Functions (Total)']) if stats['Functions (Total)'] > 0 else 1

            print(f"{stat_indent}├─ Base:  L: {stats['Total Lines (Raw)']} ({stats['Code Lines']}C, {stats['Comment Lines']}#) | Cls: {stats['Flat Classes']} Flat, {stats['Nested Classes']} Nested | Int: {stats['Interfaces']} | Enum: {stats['Enums']}")
            print(f"{stat_indent}├─ Funcs: {stats['Functions (Total)']} (Pub: {stats['Func (Pub)']}+{stats['Func (Pub Stat)']}S, Priv: {stats['Func (Priv)']}+{stats['Func (Priv Stat)']}S, Prot: {stats['Func (Prot)']}+{stats['Func (Prot Stat)']}S, Int: {stats['Func (Int)']}+{stats['Func (Int Stat)']}S)")
            print(f"{stat_indent}├─ Vars:  {stats['Variables (Total)']} (Pub: {stats['Vars (Public)']}, Priv: {stats['Vars (Private)']}, Stat: {stats['Vars (Static)']})")
            print(f"{stat_indent}└─ Evals: Max Nesting: {stats['Max Nesting Level']} | Max/Avg Complexity: {stats['Max Cyclomatic Complexity']}/{avg_comp:.1f} | Max Params: {stats['Max Parameters']}")
            print(f"{child_prefix}")

        for i, d in enumerate(dirs):
            is_last_d = (i == len(dirs) - 1)
            print_tree_node(d, children[d], child_prefix, is_last_d, is_root=False)

def print_global_summary(stats, class_registry, file_counts, alerts):
    print("="*105)
    print(" 📊 GLOBAL CODEBASE SUMMARY")
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

    print("-" * 105)
    print(" 🚀 CODE QUALITY EVALUATIONS (Highest recorded values across project)")
    print("-" * 105)
    print(f"Deepest Nest Level:          {stats['Max Nesting Level']}")
    print(f"Max Cyclomatic Complexity:   {stats['Max Cyclomatic Complexity']}")
    print(f"Largest Number of Params:    {stats['Max Parameters']}")

    print("-" * 105)
    print(" 🏆 TOP 10 LARGEST CLASSES (Combined Partial Across Files)")
    print("-" * 105)
    sorted_classes = sorted(class_registry.items(), key=lambda x: x[1], reverse=True)
    for i, (cls_name, loc) in enumerate(sorted_classes[:10]):
        print(f"{i+1:2d}. {cls_name:<65} {loc:>6} lines")

    print("-" * 105)
    print(" 📁 FILES WITH THE MOST CLASSES")
    print("-" * 105)

    sorted_files = sorted(file_counts.items(), key=lambda x: x[1]['classes'], reverse=True)

    for i, (path, data) in enumerate(sorted_files[:5]):
        filename = os.path.basename(path)
        print(f"{i+1:2d}. {filename:<65} {data['classes']:>3} classes ({data['loc']} lines)")

        if data.get('details'):
            sorted_details = sorted(data['details'], key=lambda x: x[1], reverse=True)
            details_str_list = [f"{name} ({loc}L)" for name, loc in sorted_details]
            details_str = ", ".join(details_str_list)

            wrapped = textwrap.wrap(details_str, width=85)
            for j, line in enumerate(wrapped):
                prefix = "      └─ " if j == 0 else "         "
                print(f"{prefix}{line}")

    # NEW: Actionable Alerts Section
    print("-" * 105)
    print(" ⚠️ ACTIONABLE ALERTS (Refactoring Targets)")
    print("-" * 105)
    if alerts:
        for alert in sorted(alerts):
            print(f"  * {alert}")
    else:
        print("  * None! Codebase is looking healthy against current thresholds.")

    print("="*105 + "\n")

if __name__ == "__main__":
    if len(sys.argv) > 1:
        target_directory = sys.argv[1]
    else:
        target_directory = "./EngineNet" if os.path.isdir("./EngineNet") else "./"

    if os.path.isdir(target_directory):
        print(f"\nAnalyzing structure and metrics for: {os.path.abspath(target_directory)}...")

        # Modified to unpack the new 'alerts' list
        project_tree, global_stats, class_registry, file_counts, projects_dict, alerts = build_project_tree(target_directory)

        print_project_graph(projects_dict)

        print("="*105)
        print(" 📂 FILE SYSTEM TREE")
        print("="*105)
        root_name = os.path.basename(os.path.abspath(target_directory))
        print_tree_node(root_name, project_tree, is_root=True)

        # Modified to pass the alerts list
        print_global_summary(global_stats, class_registry, file_counts, alerts)
    else:
        print(f"Invalid directory path '{target_directory}'. Please check the path and try again.")