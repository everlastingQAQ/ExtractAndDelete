#include <windows.h>
#include <appmodel.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <propkey.h>
#include <string>
#include <vector>
#include <new>
#include <cwchar>

namespace
{
    HMODULE g_module = nullptr;
    volatile long g_serverLocks = 0;
    volatile long g_objectCount = 0;
    const CLSID kCommandClsid = { 0x4f4f8f37, 0xb78c, 0x4b3d, { 0x90, 0xce, 0x8d, 0x16, 0xc4, 0x48, 0x3b, 0x8e } };
    const GUID kCanonicalName = { 0x8d50c9df, 0xf053, 0x4e87, { 0x8f, 0x4a, 0xa1, 0x47, 0x1c, 0x44, 0x37, 0x28 } };

    bool IsSupportedArchiveFile(IShellItem* item, std::wstring& path)
    {
        if (item == nullptr)
        {
            return false;
        }

        SFGAOF attributes = 0;
        if (FAILED(item->GetAttributes(SFGAO_FOLDER | SFGAO_STREAM, &attributes))
            || (attributes & SFGAO_FOLDER) != 0
            || (attributes & SFGAO_STREAM) == 0)
        {
            return false;
        }

        PWSTR displayName = nullptr;
        if (FAILED(item->GetDisplayName(SIGDN_FILESYSPATH, &displayName)) || displayName == nullptr)
        {
            return false;
        }

        path.assign(displayName);
        CoTaskMemFree(displayName);
        const wchar_t* extension = PathFindExtensionW(path.c_str());
        return extension != nullptr
            && (_wcsicmp(extension, L".zip") == 0
                || _wcsicmp(extension, L".7z") == 0
                || _wcsicmp(extension, L".rar") == 0
                || _wcsicmp(extension, L".tar") == 0);
    }

    bool GetSingleArchivePath(IShellItemArray* selection, std::wstring& path)
    {
        if (selection == nullptr)
        {
            return false;
        }

        DWORD count = 0;
        if (FAILED(selection->GetCount(&count)) || count != 1)
        {
            return false;
        }

        IShellItem* item = nullptr;
        HRESULT hr = selection->GetItemAt(0, &item);
        if (FAILED(hr) || item == nullptr)
        {
            return false;
        }

        bool valid = IsSupportedArchiveFile(item, path);
        item->Release();
        return valid;
    }

    std::wstring QuoteCommandLineArgument(const std::wstring& value)
    {
        std::wstring result = L"\"";
        size_t backslashes = 0;
        for (wchar_t character : value)
        {
            if (character == L'\\')
            {
                ++backslashes;
            }
            else if (character == L'\"')
            {
                result.append(backslashes * 2 + 1, L'\\');
                result.push_back(L'\"');
                backslashes = 0;
            }
            else
            {
                result.append(backslashes, L'\\');
                backslashes = 0;
                result.push_back(character);
            }
        }
        result.append(backslashes * 2, L'\\');
        result.push_back(L'\"');
        return result;
    }

    std::wstring GetAppUserModelId()
    {
        UINT32 length = 0;
        UINT32 count = 0;
        LONG status = GetCurrentPackageInfo(0, &length, nullptr, &count);
        if (status != ERROR_INSUFFICIENT_BUFFER || length == 0 || count == 0)
        {
            return L"ExtractAndDelete!App";
        }

        std::vector<BYTE> buffer(length);
        auto packageInfo = reinterpret_cast<PACKAGE_INFO*>(buffer.data());
        if (GetCurrentPackageInfo(0, &length, buffer.data(), &count) != ERROR_SUCCESS
            || count == 0)
        {
            return L"ExtractAndDelete!App";
        }

        UINT32 familyNameLength = 0;
        if (PackageFamilyNameFromId(&packageInfo[0].packageId, &familyNameLength, nullptr) != ERROR_INSUFFICIENT_BUFFER)
        {
            return L"ExtractAndDelete!App";
        }

        std::vector<wchar_t> familyName(familyNameLength);
        if (PackageFamilyNameFromId(&packageInfo[0].packageId, &familyNameLength, familyName.data()) != ERROR_SUCCESS)
        {
            return L"ExtractAndDelete!App";
        }

        return std::wstring(familyName.data()) + L"!App";
    }

    HRESULT ActivateGui(const std::wstring& archivePath)
    {
        IApplicationActivationManager* activationManager = nullptr;
        HRESULT hr = CoCreateInstance(
            CLSID_ApplicationActivationManager,
            nullptr,
            CLSCTX_LOCAL_SERVER,
            IID_PPV_ARGS(&activationManager));
        if (FAILED(hr) || activationManager == nullptr)
        {
            return FAILED(hr) ? hr : E_NOINTERFACE;
        }

        std::wstring arguments = L"--archive " + QuoteCommandLineArgument(archivePath);
        DWORD processId = 0;
        hr = activationManager->ActivateApplication(
            GetAppUserModelId().c_str(),
            arguments.c_str(),
            AO_NONE,
            &processId);
        activationManager->Release();
        return hr;
    }

    class ExplorerCommand final : public IExplorerCommand
    {
    public:
        ExplorerCommand()
        {
            InterlockedIncrement(&g_objectCount);
        }

        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override
        {
            if (object == nullptr)
            {
                return E_POINTER;
            }
            *object = nullptr;
            if (iid == IID_IUnknown || iid == IID_IExplorerCommand)
            {
                *object = static_cast<IExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        ULONG STDMETHODCALLTYPE AddRef() override
        {
            return static_cast<ULONG>(InterlockedIncrement(&referenceCount_));
        }

        ULONG STDMETHODCALLTYPE Release() override
        {
            ULONG count = static_cast<ULONG>(InterlockedDecrement(&referenceCount_));
            if (count == 0)
            {
                delete this;
            }
            return count;
        }

        HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray*, LPWSTR* title) override
        {
            if (title == nullptr)
            {
                return E_POINTER;
            }
            *title = static_cast<LPWSTR>(CoTaskMemAlloc(sizeof(wchar_t) * 6));
            if (*title == nullptr)
            {
                return E_OUTOFMEMORY;
            }
            wcscpy_s(*title, 6, L"解压并回收");
            return S_OK;
        }

        HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*, LPWSTR* icon) override
        {
            if (icon == nullptr)
            {
                return E_POINTER;
            }
            const wchar_t* value = L"%SystemRoot%\\System32\\shell32.dll,-16769";
            size_t length = wcslen(value) + 1;
            *icon = static_cast<LPWSTR>(CoTaskMemAlloc(sizeof(wchar_t) * length));
            if (*icon == nullptr)
            {
                return E_OUTOFMEMORY;
            }
            wcscpy_s(*icon, length, value);
            return S_OK;
        }

        HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*, LPWSTR* tooltip) override
        {
            if (tooltip == nullptr)
            {
                return E_POINTER;
            }
            *tooltip = nullptr;
            return E_NOTIMPL;
        }

        HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* name) override
        {
            if (name == nullptr)
            {
                return E_POINTER;
            }
            *name = kCanonicalName;
            return S_OK;
        }

        HRESULT STDMETHODCALLTYPE GetState(IShellItemArray* selection, BOOL, EXPCMDSTATE* state) override
        {
            if (state == nullptr)
            {
                return E_POINTER;
            }
            std::wstring path;
            *state = GetSingleArchivePath(selection, path) ? ECS_ENABLED : ECS_DISABLED;
            return S_OK;
        }

        HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* selection, IBindCtx*) override
        {
            std::wstring path;
            if (!GetSingleArchivePath(selection, path))
            {
                return E_INVALIDARG;
            }
            return ActivateGui(path);
        }

        HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* flags) override
        {
            if (flags == nullptr)
            {
                return E_POINTER;
            }
            *flags = ECF_DEFAULT;
            return S_OK;
        }

        HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            if (commands != nullptr)
            {
                *commands = nullptr;
            }
            return E_NOTIMPL;
        }

    private:
        ~ExplorerCommand()
        {
            InterlockedDecrement(&g_objectCount);
        }
        volatile long referenceCount_ = 1;
    };

    class ClassFactory final : public IClassFactory
    {
    public:
        ClassFactory()
        {
            InterlockedIncrement(&g_objectCount);
        }

        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override
        {
            if (object == nullptr)
            {
                return E_POINTER;
            }
            *object = nullptr;
            if (iid == IID_IUnknown || iid == IID_IClassFactory)
            {
                *object = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        ULONG STDMETHODCALLTYPE AddRef() override
        {
            return static_cast<ULONG>(InterlockedIncrement(&referenceCount_));
        }

        ULONG STDMETHODCALLTYPE Release() override
        {
            ULONG count = static_cast<ULONG>(InterlockedDecrement(&referenceCount_));
            if (count == 0)
            {
                delete this;
            }
            return count;
        }

        HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** object) override
        {
            if (object == nullptr)
            {
                return E_POINTER;
            }
            *object = nullptr;
            if (outer != nullptr)
            {
                return CLASS_E_NOAGGREGATION;
            }
            ExplorerCommand* command = new (std::nothrow) ExplorerCommand();
            if (command == nullptr)
            {
                return E_OUTOFMEMORY;
            }
            HRESULT hr = command->QueryInterface(iid, object);
            command->Release();
            return hr;
        }

        HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override
        {
            if (lock)
            {
                InterlockedIncrement(&g_serverLocks);
            }
            else
            {
                InterlockedDecrement(&g_serverLocks);
            }
            return S_OK;
        }

    private:
        ~ClassFactory()
        {
            InterlockedDecrement(&g_objectCount);
        }
        volatile long referenceCount_ = 1;
    };
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

extern "C" HRESULT __stdcall ExtractAndDeleteDllCanUnloadNow()
{
    return g_serverLocks == 0 && g_objectCount == 0 ? S_OK : S_FALSE;
}

extern "C" HRESULT __stdcall ExtractAndDeleteDllGetClassObject(
    REFCLSID clsid,
    REFIID iid,
    void** object)
{
    if (object == nullptr)
    {
        return E_POINTER;
    }
    *object = nullptr;
    if (clsid != kCommandClsid)
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    ClassFactory* factory = new (std::nothrow) ClassFactory();
    if (factory == nullptr)
    {
        return E_OUTOFMEMORY;
    }
    HRESULT hr = factory->QueryInterface(iid, object);
    factory->Release();
    return hr;
}
