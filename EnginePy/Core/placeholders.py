#Engine\Core\placeholders.py
import re
from typing import Any, Dict, List


_placeholder_re = re.compile(r"\{\{([\w\.]+)\}\}")


def resolve_placeholders(value: Any, context: Dict[str, Any]):
	if isinstance(value, dict):
		return {k: resolve_placeholders(v, context) for k, v in value.items()}
	if isinstance(value, list):
		return [resolve_placeholders(v, context) for v in value]
	if not isinstance(value, str):
		return value


	def repl(m):
		current = context
		for key in m.group(1).split('.'):
			try:
				current = current[key]
			except (KeyError, TypeError):
				return m.group(0)
		return str(current)


	return _placeholder_re.sub(repl, value)
