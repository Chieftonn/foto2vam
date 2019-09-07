import os
import sys
from distutils.dir_util import copy_tree

from_dir = 'Output/'
to_dir = r'H:\Games\VAM\1.7\Saves\Person\foto2vam'
for folder in sys.argv:
    from_path = os.path.join(from_dir, folder)
    if not os.path.isdir(from_path):
        continue
    to_path = os.path.join(to_dir, folder)

    copy_tree(from_path, to_path)
