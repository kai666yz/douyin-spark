"""Compatibility entry: run the native WebView2 + Python backend integration checks."""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).with_name('verify_group_backend.py')),run_name='__main__')
