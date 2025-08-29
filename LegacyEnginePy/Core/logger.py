# Engine\Core\logger.py
import logging
from pathlib import Path


def build_operations_logger(log_path: Path | str = "main.operation.log") -> logging.Logger:
	logger = logging.getLogger('operation_logger')
	logger.setLevel(logging.INFO)
	if logger.hasHandlers():
		logger.handlers.clear()
	handler = logging.FileHandler(str(log_path), mode='a', encoding='utf-8')
	formatter = logging.Formatter('%(asctime)s - %(message)s', datefmt='%Y-%m-%d %H:%M:%S')
	handler.setFormatter(formatter)
	logger.addHandler(handler)
	return logger