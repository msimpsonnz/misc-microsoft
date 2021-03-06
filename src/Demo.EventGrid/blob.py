import os
import uuid
import sys
import time
from azure.storage import CloudStorageAccount
from azure.storage.blob import BlockBlobService

try:
    for ar in range(0, len(sys.argv)):
        assert sys.argv[ar] is not None
    container_name = sys.argv[1]
    num = int(sys.argv[2])
    clean = int(sys.argv[3])
except:
    print "No args supplied"

try:
    account_name = os.environ['AZURE_STORAGE_ACCOUNT']
    account_key = os.environ['AZURE_STORAGE_ACCESS_KEY']
    account = CloudStorageAccount(account_name, account_key)
    assert account_name is not None
    assert account_key is not None
    account is not None
except:
    print "Error getting Azure details"

def list_blobs(account, container_name):  
        # Create a Block Blob Service object
        blockblob_service = account.create_block_blob_service()
        generator = blockblob_service.list_blobs(container_name)
        for blob in generator:
            print('\tBlob Name: ' + blob.name)

def create_blobs(account, container_name, num):
        # Create a Block Blob Service object
        blockblob_service = account.create_block_blob_service()
        for x in range(0, num):
            name = str(uuid.uuid4()) + '-file.txt'
            with open(name, "w") as f:
                f.write("")
            full_path_to_file = os.path.join(os.path.dirname(__file__), name)
            blockblob_service.create_blob_from_path(container_name, name, full_path_to_file)
            print('\tUploading: ' + name)
            os.remove(name)
            time.sleep(20)

def delete_blobs(account, container_name):   
        # Create a Block Blob Service object
        blockblob_service = account.create_block_blob_service()
        generator = blockblob_service.list_blobs(container_name)
        for blob in generator:
            print('\tBlob Name: ' + blob.name)
            blockblob_service.delete_blob(container_name, blob.name)



if (clean == 1):
    delete_blobs(account, container_name)
else:
    create_blobs(account, container_name, num)

list_blobs(account, container_name)