"""Qt integration checks: panel ownership, mode transitions, reuse of send callback."""
import os
os.environ.setdefault('QT_QPA_PLATFORM', 'offscreen')
import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'overlay'))
from PySide6.QtWidgets import QApplication, QWidget, QVBoxLayout, QFrame, QTextEdit, QPushButton
from zheli_shell import install


class Pet(QWidget):
    def __init__(self):
        super().__init__()
        self.layout = QVBoxLayout(self)
        self.chat_panel = QFrame(self)
        self.chat_panel.setFixedSize(420, 240)
        self.layout.addWidget(self.chat_panel)
        body = QVBoxLayout(self.chat_panel)
        self.side_panel = QWidget()
        self.chat_input = QTextEdit()
        body.addWidget(self.side_panel)
        body.addWidget(self.chat_input)
        self.sent = []

    def _on_chat_input(self):
        self.sent.append(self.chat_input.toPlainText())

    def _pick_attach_files(self): pass
    def _open_memory_manager(self): pass
    def _export_chat(self): pass
    def _open_settings(self): pass


class ShellTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = QApplication.instance() or QApplication([])

    def test_switch_reuses_conversation_and_native_resize(self):
        pet = Pet()
        shell = install(pet)
        original = pet.chat_input
        shell.present('quick')
        self.assertIs(pet.chat_panel.parent(), shell)
        self.assertFalse(shell.navigation.isVisible())
        self.assertFalse(pet.side_panel.isVisible())
        pet.chat_input.setPlainText('测试内容')
        shell.present('full')
        self.assertTrue(shell.navigation.isVisible())
        self.assertTrue(pet.side_panel.isVisible())
        self.assertIs(pet.chat_input, original)
        self.assertEqual(pet.chat_input.toPlainText(), '测试内容')
        self.assertGreater(pet.chat_panel.maximumWidth(), 420)
        send = next(b for b in shell.findChildren(QPushButton) if b.text().startswith('发送'))
        send.click()
        self.assertEqual(pet.sent, ['测试内容'])
        shell.close()
        self.assertFalse(shell.isVisible())
        self.assertEqual(shell.mode, 'pet')
        pet.toggle_chat_panel()
        self.assertTrue(shell.isVisible())
        self.assertEqual(shell.mode, 'quick')
        shell.close()
        shell.deleteLater()
        pet.deleteLater()
        self.app.processEvents()

    @unittest.skipUnless(sys.platform == 'win32', 'full upstream runtime requires Windows')
    def test_real_upstream_widgets(self):
        sys.path.insert(0, str(Path(__file__).resolve().parents[1] / '_upstream'))
        from desktop_pet import PetWidget
        pet = PetWidget()
        original_panel = pet.chat_panel
        shell = install(pet)
        shell.present('full')
        self.app.processEvents()
        self.assertIs(pet.chat_panel, original_panel)
        self.assertIs(pet.chat_history_scroll.window(), shell)
        self.assertIs(pet.chat_input.window(), shell)
        self.assertTrue(pet.chat_input.isVisible())
        self.assertGreater(pet.chat_input.width(), 100)
        shell.present('quick')
        self.app.processEvents()
        self.assertFalse(pet.side_panel.isVisible())
        self.assertTrue(pet.chat_input.isVisible())
        shell.close()
        pet.close()


if __name__ == '__main__':
    unittest.main()
